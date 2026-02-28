global using static eft_dma_radar.Tarkov.MemoryInterface;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using IL2CPP = eft_dma_radar.Tarkov.Unity.IL2CPP;
using Vmmsharp;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;

namespace eft_dma_radar.Tarkov
{
    internal static class MemoryInterface
    {
        private static MemDMA _actualMemory;
        private static SafeMemoryProxy _safeMemory;

        /// <summary>
        /// Safe Memory Interface that works in both Normal and Safe mode.
        /// </summary>
        public static SafeMemoryProxy Memory
        {
            get
            {
                // Safety net: if Memory is accessed before ModuleInit, create a safe proxy
                if (_safeMemory == null)
                {
                    XMLogging.WriteLine("[Warning] Memory accessed before ModuleInit - creating emergency safe proxy");
                    _safeMemory = new SafeMemoryProxy(null);
                }

                return _safeMemory;
            }
        }

        /// <summary>
        /// Check if DMA is available.
        /// </summary>
        public static bool IsDMAAvailable =>
            _actualMemory != null &&
            Program.CurrentMode == ApplicationMode.Normal;

        /// <summary>
        /// Initialize the Memory Interface. Safe to call more than once.
        /// </summary>
        public static void ModuleInit()
        {
            // Idempotent init
            if (_safeMemory != null && _actualMemory != null && Program.CurrentMode == ApplicationMode.Normal)
                return;

            if (Program.CurrentMode == ApplicationMode.Normal)
            {
                _actualMemory = new MemDMA();
                _safeMemory = new SafeMemoryProxy(_actualMemory);
                XMLogging.WriteLine("DMA Memory Interface initialized - Normal Mode");
            }
            else
            {
                _actualMemory = null;
                _safeMemory = new SafeMemoryProxy(null);
                XMLogging.WriteLine("Safe Memory Interface initialized - Safe Mode (DMA disabled)");
            }
        }
    }

    /// <summary>
    /// DMA Memory Module.
    /// </summary>
    public sealed class MemDMA : MemDMABase
    {
        #region Fields/Properties/Constructor/Thread Worker

        private const string _processName = "EscapeFromTarkov.exe";

        /// <summary>
        /// App Configuration.
        /// </summary>
        private static Config Config => Program.Config;

        /// <summary>Current Map ID.</summary>
        public string MapID => Game?.MapID;

        public override bool IsOffline      => LocalGameWorld.IsOffline;
        public override ulong LevelSettings => LocalGameWorld.LevelSettings;

        /// <summary>True if currently in a raid/match, otherwise False.</summary>
        public override bool InRaid         => Game?.InRaid ?? false;

        /// <summary>True if the raid countdown has completed and the raid has started.</summary>
        public override bool RaidHasStarted => Game?.RaidHasStarted ?? false;

        private bool _ready;
        /// <summary>True if Startup was successful and waiting for raid.</summary>
        public override bool Ready => _ready;

        private bool _starting;
        /// <summary>True if in the process of starting the game.</summary>
        public override bool Starting => _starting;

        /// <summary>
        /// Last map ID seen by the game loop, used to detect in-raid map transitions.
        /// </summary>
        private string _lastMapIdForMemWrites;

        public IReadOnlyCollection<Player>         Players    => Game?.Players;
        public IReadOnlyCollection<IExplosiveItem> Explosives => Game?.Explosives;
        public IReadOnlyCollection<IExitPoint>     Exits      => Game?.Exits;

        public LocalPlayer  LocalPlayer  => Game?.LocalPlayer;
        public LootManager  Loot         => Game?.Loot;
        public QuestManager QuestManager => Game?.QuestManager;
        public LocalGameWorld Game       { get; private set; }

        /// <summary>IL2CPP GameObjectManager address.</summary>
        public ulong GOM { get; private set; }

        /// <summary>GameAssembly.dll base address (IL2CPP binary).</summary>
        public ulong GameAssemblyBase { get; private set; }

        public MemDMA() : base(Config.FpgaAlgo, Config.MemMapEnabled)
        {
            GameStarted  += MemDMA_GameStarted;
            GameStopped  += MemDMA_GameStopped;
            RaidStarted  += MemDMA_RaidStarted;
            RaidStopped  += MemDMA_RaidStopped;

            new Thread(MemoryPrimaryWorker)
            {
                IsBackground = true
            }.Start();
        }

        /// <summary>
        /// Main worker thread to perform DMA Reads on.
        /// </summary>
        private void MemoryPrimaryWorker()
        {
            XMLogging.WriteLine("Memory thread starting...");

            while (!MainWindow.Initialized)
            {
                XMLogging.WriteLine("[Waiting] Main window not ready...");
                Thread.Sleep(100);
            }

            while (true)
            {
                try
                {
                    RunStartupLoop();
                    OnGameStarted();
                    RunGameLoop();
                    OnGameStopped();
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"FATAL ERROR on Memory Thread: {ex}");
                    if (MainWindow.Window != null)
                        NotificationsShared.Warning("FATAL ERROR on Memory Thread");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Startup / Main Loop

        /// <summary>
        /// Starts up the Game Process and all mandatory modules.
        /// Returns when the Game is ready.
        /// </summary>
        private void RunStartupLoop()
        {
            XMLogging.WriteLine("New Game Startup");

            while (true) // Startup loop
            {
                try
                {
                    FullRefresh();
                    ResourceJanitor.Run();
                    LoadProcess();
                    LoadModules();

                    _starting = true;

                    InputManager.Initialize();
                    CameraManager.Initialize(); // IL2CPP ported - signature scan
                    _ready = true;

                    XMLogging.WriteLine("Game Startup [OK]");
                    if (MainWindow.Window != null)
                        NotificationsShared.Info("Game Startup [OK]");
                    return;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"Game Startup [FAIL]: {ex}");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        #region Restart Radar

        private readonly Lock _restartSync = new();
        private CancellationTokenSource _radarCts = new();

        /// <summary>
        /// Signal the Radar to restart the raid/game loop.
        /// </summary>
        public void RequestRestart()
        {
            lock (_restartSync)
            {
                var old = Interlocked.Exchange(ref _radarCts, new CancellationTokenSource());
                old.Cancel();
                old.Dispose();
            }
        }

        #endregion

        /// <summary>
        /// Main Game Loop Method.
        /// Returns to caller when Game is no longer running.
        /// </summary>
        private void RunGameLoop()
        {
            while (true)
            {
                LocalGameWorld.RaidCooldown.WaitIfActive(_radarCts.Token);

                try
                {
                    var ct = _radarCts.Token;

                    using (var game = Game = LocalGameWorld.CreateGameInstance(ct))
                    {
                        // New raid / session begins
                        _lastMapIdForMemWrites = game.MapID;
                        XMLogging.WriteLine($"[MemDMA] New GameInstance created. Map = '{_lastMapIdForMemWrites}'");

                        OnRaidStarted();
                        game.Start();

                        while (game.InRaid)
                        {
                            ct.ThrowIfCancellationRequested();

                            // 1) Detect in-raid map transitions (EFT transit)
                            var currentMapId = game.MapID;

                            if (!string.IsNullOrEmpty(currentMapId) &&
                                !string.IsNullOrEmpty(_lastMapIdForMemWrites) &&
                                !string.Equals(currentMapId, _lastMapIdForMemWrites, StringComparison.Ordinal))
                            {
                                XMLogging.WriteLine(
                                    $"[MemDMA] Map transition detected: '{_lastMapIdForMemWrites}' -> '{currentMapId}'. " +
                                    "Resetting memwrite features / caches.");

                                OnRaidStopped();   // IFeature.OnRaidEnd()
                                OnRaidStarted();   // IFeature.OnRaidStart()

                                _lastMapIdForMemWrites = currentMapId;
                            }

                            // 2) User-requested restart
                            if (_restartRadar)
                            {
                                XMLogging.WriteLine("Restarting Radar per User Request.");
                                if (MainWindow.Window != null)
                                    NotificationsShared.Info("Restarting Radar per User Request.");

                                _restartRadar = false;
                                RequestRestart();    // cancel token, will restart outer loop
                                break;
                            }

                            // 3) Normal refresh
                            game.Refresh();
                            Thread.Sleep(133);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    XMLogging.WriteLine("Radar restart requested.");
                    continue;
                }
                catch (GameNotRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"CRITICAL ERROR in Game Loop: {ex}");
                    if (MainWindow.Window != null)
                        NotificationsShared.Warning("CRITICAL ERROR in Game Loop");
                    break;
                }
                finally
                {
                    OnRaidStopped();
                    Thread.Sleep(100);
                }
            }

            XMLogging.WriteLine("Game is no longer running!");
            if (MainWindow.Window != null)
                NotificationsShared.Warning("Game is no longer running!");
        }

        #endregion

        #region Event handlers

        private void MemDMA_GameStarted(object sender, EventArgs e)
        {
            _syncProcessRunning.Set();
        }

        private void MemDMA_GameStopped(object sender, EventArgs e)
        {
            _restartRadar = default;
            _starting     = default;
            _ready        = default;
            UnityBase     = default;
            MonoBase      = default;
            GOM           = default;

            _syncProcessRunning.Reset();
        }

        private void MemDMA_RaidStopped(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            _syncInRaid.Reset();
            Game = null;
        }

        private void MemDMA_RaidStarted(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            _syncInRaid.Set();
        }

        #endregion

        #region Process / Modules

        /// <summary>Obtain the PID for the Game Process.</summary>
        private void LoadProcess()
        {
            var tmpProcess = _hVMM.Process(_processName);
            if (tmpProcess == null)
                throw new Exception($"Unable to find '{_processName}'");

            Process = tmpProcess;
        }

        /// <summary>
        /// Loads required game modules and initializes IL2CPP structures.
        /// </summary>
        private void LoadModules()
        {
            // UnityPlayer.dll base
            var unityBase = Process.GetModuleBase("UnityPlayer.dll");
            ArgumentOutOfRangeException.ThrowIfZero(unityBase, nameof(unityBase));
            UnityBase = unityBase;

            // GameAssembly.dll base (IL2CPP)
            var gameAssemblyBase = Process.GetModuleBase("GameAssembly.dll");
            if (gameAssemblyBase != 0)
            {
                GameAssemblyBase = gameAssemblyBase;
                XMLogging.WriteLine($"[IL2CPP] GameAssembly.dll base: 0x{gameAssemblyBase:X}");
            }
            else
            {
                XMLogging.WriteLine("[IL2CPP] WARNING: GameAssembly.dll not found!");
            }

            // IL2CPP GameObjectManager (signature scan + fallback)
            GOM = IL2CPP.GameObjectManager.GetAddr(unityBase);
            ArgumentOutOfRangeException.ThrowIfZero(GOM, nameof(GOM));
            XMLogging.WriteLine($"[IL2CPP] GOM Address: 0x{GOM:X}");

            // Mono is DEPRECATED - IL2CPP only
            MonoBase = 0;
        }

        #endregion

        #region R/W Methods

        public void WriteValue<T>(LocalGameWorld game, ulong addr, T value)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem)
                throw new Exception("Not safe to write!");

            WriteValue(addr, value);
        }

        public void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem)
                throw new Exception("Not safe to write!");

            WriteBuffer(addr, buffer);
        }

        public bool TryReadValue<T>(ulong addr, out T value) where T : unmanaged
        {
            try
            {
                value = Memory.ReadValue<T>(addr);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public bool IsValid(ulong address)
        {
            try
            {
                if (address == 0)
                    return false;

                _ = Memory.ReadValue<byte>(address);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Misc

        /// <summary>
        /// Throws a special exception if no longer in game.
        /// </summary>
        public void ThrowIfNotInGame()
        {
            FullRefresh();

            for (var i = 0; i < 5; i++)
            {
                try
                {
                    var tempProcess = _hVMM.Process(_processName);
                    if (tempProcess is null)
                        throw new Exception();

                    return;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }

            throw new GameNotRunning("Not in game!");
        }

        public Rectangle GetMonitorRes()
        {
            try
            {
                var gfx = ReadPtr(UnityBase + UnityOffsets.ModuleBase.GfxDevice, false);
                var res = ReadValue<Rectangle>(gfx + UnityOffsets.GfxDeviceClient.Viewport, false);

                if (res.Width  <= 0 || res.Width  > 10000 ||
                    res.Height <= 0 || res.Height > 5000)
                {
                    throw new ArgumentOutOfRangeException(nameof(res));
                }

                return res;
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Getting Game Monitor Res", ex);
            }
        }

        public sealed class GameNotRunning : Exception
        {
            public GameNotRunning() { }

            public GameNotRunning(string message) : base(message) { }

            public GameNotRunning(string message, Exception inner) : base(message, inner) { }
        }

        #endregion
    }

    /// <summary>
    /// Proxy around MemDMA that gracefully degrades when DMA is not available.
    /// </summary>
    public class SafeMemoryProxy
    {
        private readonly MemDMA _actualMemory;
        private bool _loggedSafeWarning;

        private bool HasDMA => _actualMemory != null;

        private void LogSafeOnce(string message)
        {
            if (_loggedSafeWarning)
                return;

            _loggedSafeWarning = true;
            XMLogging.WriteLine(message);
        }

        public SafeMemoryProxy(MemDMA actualMemory)
        {
            _actualMemory = actualMemory;
        }

        public QuestManager                  QuestManager => _actualMemory?.QuestManager;
        public LootManager                   Loot         => _actualMemory?.Loot;
        public LocalPlayer                   LocalPlayer  => _actualMemory?.LocalPlayer;
        public IReadOnlyCollection<Player>   Players      => _actualMemory?.Players      ?? new List<Player>();
        public IReadOnlyCollection<IExplosiveItem> Explosives => _actualMemory?.Explosives ?? new List<IExplosiveItem>();
        public IReadOnlyCollection<IExitPoint>     Exits      => _actualMemory?.Exits      ?? new List<IExitPoint>();
        public LocalGameWorld               Game          => _actualMemory?.Game;
        public string                       MapID         => _actualMemory?.MapID;
        public bool                         IsOffline     => _actualMemory?.IsOffline ?? false;
        public ulong                        LevelSettings => _actualMemory?.LevelSettings ?? 0;
        public bool                         InRaid        => _actualMemory?.InRaid ?? false;
        public bool                         RaidHasStarted => _actualMemory?.RaidHasStarted ?? false;
        public bool                         Ready          => _actualMemory?.Ready ?? false;
        public ulong                        GameAssemblyBase => _actualMemory?.GameAssemblyBase ?? 0;
        public bool                         Starting         => _actualMemory?.Starting ?? false;

        public ulong      MonoBase  => _actualMemory?.MonoBase ?? 0;
        public ulong      UnityBase => _actualMemory?.UnityBase ?? 0;
        public ulong      GOM       => _actualMemory?.GOM ?? 0;
        public VmmProcess Process   => _actualMemory?.Process;
        public Vmm        VmmHandle => _actualMemory?.VmmHandle;

        public bool RestartRadar
        {
            set
            {
                if (HasDMA)
                    _actualMemory.RestartRadar = value;
            }
        }

        #region Read helpers

        public bool TryReadValue<T>(ulong addr, out T value) where T : unmanaged
        {
            if (HasDMA)
                return _actualMemory.TryReadValue(addr, out value);

            value = default;
            return false;
        }

        public bool IsValid(ulong address) =>
            HasDMA && _actualMemory.IsValid(address);

        public T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] ReadValue<{typeof(T).Name}> skipped (DMA disabled)");
                return default;
            }

            return _actualMemory.ReadValue<T>(addr, useCache);
        }

        public void ReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                result = default;
                return;
            }

            _actualMemory.ReadValue(addr, out result, useCache);
        }

        public T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] ReadValueEnsure<{typeof(T).Name}> skipped (DMA disabled)");
                return default;
            }

            return _actualMemory.ReadValueEnsure<T>(addr);
        }

        public void ReadValueEnsure<T>(ulong addr, out T result)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                result = default;
                return;
            }

            _actualMemory.ReadValueEnsure(addr, out result);
        }

        public ulong ReadPtr(ulong addr, bool useCache = true)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadPtr skipped (DMA disabled)");
                return 0;
            }

            return _actualMemory.ReadPtr(addr, useCache);
        }

        public ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadPtrChain skipped (DMA disabled)");
                return 0;
            }

            return _actualMemory.ReadPtrChain(addr, offsets, useCache);
        }

        public void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] ReadBuffer<{typeof(T).Name}> skipped (DMA disabled)");
                buffer.Clear();
                return;
            }

            _actualMemory.ReadBuffer(addr, buffer, useCache, allowPartialRead);
        }

        public T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] ReadArray<{typeof(T).Name}> skipped (DMA disabled)");
                return Array.Empty<T>();
            }

            return _actualMemory.ReadArray<T>(addr, count, useCache);
        }

        /// <summary>
        /// Read memory into a buffer.
        /// </summary>
        public byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadBuffer(byte[]) skipped (DMA disabled)");
                return Array.Empty<byte>();
            }

            return _actualMemory.ReadBuffer(addr, size, useCache, allowIncompleteRead);
        }

        public void ReadBufferEnsure<T>(ulong addr, Span<T> buffer1) where T : unmanaged
        {
            if (!HasDMA)
            {
                buffer1.Clear();
                return;
            }

            _actualMemory.ReadBufferEnsure(addr, buffer1);
        }
        public ulong FindDataXref(ulong targetAddress)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] FindDataXref skipped (DMA disabled)");
                return 0;
            }

            return _actualMemory.FindDataXref(targetAddress);
            
        }
        /// <summary>
        /// Read memory into a buffer and validate the right bytes were received.
        /// </summary>
        public byte[] ReadBufferEnsureE(ulong addr, int size)
        {
            if (!HasDMA || Process == null)
            {
                LogSafeOnce("[SafeMode] ReadBufferEnsureE skipped (DMA disabled)");
                return null;
            }

            const int ValidationCount = 3;

            try
            {
                byte[][] buffers = new byte[ValidationCount][];

                for (int i = 0; i < ValidationCount; i++)
                {
                    buffers[i] = Process.MemRead(addr, (uint)size, Vmm.FLAG_NOCACHE);

                    if (buffers[i].Length != size)
                        throw new Exception("Incomplete memory read!");
                }

                for (int i = 1; i < ValidationCount; i++)
                {
                    if (!buffers[i].SequenceEqual(buffers[0]))
                    {
                        XMLogging.WriteLine($"[WARN] ReadBufferEnsure() -> 0x{addr:X} did not pass validation!");
                        return null;
                    }
                }

                return buffers[0];
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }

        public string ReadString(ulong addr, int length, bool useCache = true)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadString skipped (DMA disabled)");
                return string.Empty;
            }

            return _actualMemory.ReadString(addr, length, useCache);
        }

        public string ReadUnityString(ulong addr, int length = 64, bool useCache = true)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadUnityString skipped (DMA disabled)");
                return string.Empty;
            }

            return _actualMemory.ReadUnityString(addr, length, useCache);
        }

        #endregion

        #region Write helpers

        public void WriteValue<T>(LocalGameWorld game, ulong addr, T value)
            where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteValue<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteValue(game, addr, value);
        }

        public void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteValue<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteValue(addr, value);
        }

        public void WriteValue<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteValue<{typeof(T).Name}> (ref) skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteValue(addr, ref value);
        }

        public void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteValueEnsure<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteValueEnsure(addr, value);
        }

        public void WriteValueEnsure<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteValueEnsure<{typeof(T).Name}> (ref) skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteValueEnsure(addr, ref value);
        }

        public void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteBuffer<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteBuffer(game, addr, buffer);
        }

        public void WriteBuffer<T>(ulong addr, Span<T> buffer) where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteBuffer<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteBuffer(addr, buffer);
        }

        public void WriteBufferEnsure<T>(ulong addr, Span<T> buffer) where T : unmanaged
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] WriteBufferEnsure<{typeof(T).Name}> skipped (DMA disabled)");
                return;
            }

            _actualMemory.WriteBufferEnsure(addr, buffer);
        }
        /// <summary>
        /// Enables or disables a Unity Behaviour via memwrite.
        /// Equivalent to Behaviour.enabled (bypasses callbacks).
        /// </summary>
        public void WriteBehaviourEnabled(LocalGameWorld game, ulong behaviour, bool enabled)
        {
            behaviour.ThrowIfInvalidVirtualAddress();
        
            // IL2CPP Behaviour.enabled ¡ú byte
            const uint BEHAVIOUR_ENABLED_OFFSET = 0x10;
        
            WriteValue(game, behaviour + BEHAVIOUR_ENABLED_OFFSET, enabled ? (byte)1 : (byte)0);
        }
        
        /// <summary>
        /// Enables or disables a GameObject via memwrite.
        /// Equivalent to GameObject.SetActive (bypasses Awake/OnEnable).
        /// </summary>
        public void WriteGameObjectActive(LocalGameWorld game, ulong gameObject, bool active)
        {
            gameObject.ThrowIfInvalidVirtualAddress();
        
            // IL2CPP GameObject active flag
            const uint GAMEOBJECT_ACTIVE_OFFSET = 0x18;
        
            WriteValue(game, gameObject + GAMEOBJECT_ACTIVE_OFFSET, active ? (byte)1 : (byte)0);
        }
        #endregion

        #region Scatter / misc

        public void ReadScatter(IScatterEntry[] entries, bool useCache = true)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadScatter skipped (DMA disabled)");
                if (entries != null)
                {
                    foreach (var entry in entries)
                        entry.IsFailed = true;
                }
                return;
            }

            _actualMemory.ReadScatter(entries, useCache);
        }

        public void ReadCache(params ulong[] va)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ReadCache skipped (DMA disabled)");
                return;
            }

            _actualMemory.ReadCache(va);
        }

        public void FullRefresh()
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] FullRefresh skipped (DMA disabled)");
                return;
            }

            _actualMemory.FullRefresh();
        }

        public ulong GetExport(string module, string name)
        {
            if (!HasDMA)
            {
                LogSafeOnce($"[SafeMode] GetExport skipped for {module}::{name} (DMA disabled)");
                return 0;
            }

            return _actualMemory.GetExport(module, name);
        }

        public VmmScatterMemory GetScatter(uint flags)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] GetScatter skipped (DMA disabled)");
                return null;
            }

            return _actualMemory.GetScatter(flags);
        }

        public ulong FindSignature(string signature, string moduleName)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] FindSignature skipped (DMA disabled)");
                return 0;
            }

            return _actualMemory.FindSignature(signature, moduleName);
        }

        public ulong FindSignature(string signature, ulong rangeStart, ulong rangeEnd, VmmProcess process)
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] FindSignature (range) skipped (DMA disabled)");
                return 0;
            }

            return _actualMemory.FindSignature(signature, rangeStart, rangeEnd, process);
        }

        public void ThrowIfNotInGame()
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] ThrowIfNotInGame skipped (DMA disabled)");
                return;
            }

            _actualMemory.ThrowIfNotInGame();
        }

        public Rectangle GetMonitorRes()
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] GetMonitorRes returning default resolution");
                return new Rectangle(0, 0, 1920, 1080);
            }

            return _actualMemory.GetMonitorRes();
        }

        public void CloseFPGA()
        {
            if (!HasDMA)
            {
                LogSafeOnce("[SafeMode] CloseFPGA skipped (DMA disabled)");
                return;
            }

            _actualMemory.CloseFPGA();
        }

        #endregion
    }  
}
