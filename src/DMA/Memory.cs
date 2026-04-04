global using eft_dma_radar.DMA;

using eft_dma_radar.DMA.ScatterAPI;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.UI.Misc;
using System.IO;
using System.Runtime;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;
using IL2CPP = eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.Tarkov.GameWorld.Loot;

namespace eft_dma_radar.DMA
{
    /// <summary>
    /// DMA Memory Module.
    /// </summary>
    internal static class Memory
    {
        #region Init

        private const string _processName = "EscapeFromTarkov.exe";
        private const string _memoryMapFile = "mmap.txt";
        public const uint MAX_READ_SIZE = (uint)0x1000 * 1500;

        private static Vmm _vmm;
        private static uint _pid;
        private static bool _restartRadar;

        private static readonly ManualResetEvent _syncProcessRunning = new(false);
        private static readonly ManualResetEvent _syncInRaid = new(false);

        private static bool _ready;
        private static bool _starting;
        private static string _lastMapIdForMemWrites;
        private static readonly Lock _restartSync = new();
        private static CancellationTokenSource _radarCts = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VmmFlags ToVmmFlags(bool useCache) =>
            useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;

        public static string MapID => Game?.MapID;
        public static ulong UnityBase { get; private set; }
        public static ulong GOM { get; private set; }
        public static ulong GameAssemblyBase { get; private set; }
        public static uint ProcessPID => _pid;
        public static Vmm VmmHandle => _vmm;
        public static bool IsDisposed => _vmm is null;
        public static bool Ready => _ready;
        public static bool Starting => _starting;
        public static bool InRaid => Game?.InRaid ?? false;
        public static bool IsOffline => LocalGameWorld.IsOffline;
        public static ulong LevelSettings => LocalGameWorld.LevelSettings;

        public static IReadOnlyCollection<Player> Players => Game?.Players;
        public static IReadOnlyCollection<IExplosiveItem> Explosives => Game?.Explosives;
        public static IReadOnlyCollection<IExitPoint> Exits => Game?.Exits;
        public static LocalPlayer LocalPlayer => Game?.LocalPlayer;
        public static LootManager Loot => Game?.Loot;
        public static QuestManager QuestManager => Game?.QuestManager;
        public static LocalGameWorld Game { get; private set; }

        public static bool RestartRadar
        {
            set
            {
                if (InRaid)
                    _restartRadar = value;
            }
        }

        /// <summary>
        /// Initialize the Memory module. Called from Program startup.
        /// </summary>
        public static void ModuleInit()
        {
            if (Program.CurrentMode == ApplicationMode.Normal)
            {
                Init();
                Log.WriteLine("DMA Memory Interface initialized - Normal Mode");
            }
            else
            {
                Log.WriteLine("Safe Memory Interface initialized - Safe Mode (DMA disabled)");
            }
        }

        private static void Init()
        {
            Log.WriteLine("Initializing DMA...");
            var vmmVersion = FileVersionInfo.GetVersionInfo("vmm.dll").FileVersion;
            var lcVersion = FileVersionInfo.GetVersionInfo("leechcore.dll").FileVersion;

            var deviceStr = Program.Config.DeviceStr;
            var useMemMap = Program.Config.MemMapEnabled;

            var initArgs = new string[]
            {
                "-norefresh",
                "-device",
                deviceStr,
                "-waitinitialize"
            };

            try
            {
                if (useMemMap && !File.Exists(_memoryMapFile))
                {
                    Log.WriteLine("[DMA] No MemMap, attempting to generate...");
                    _vmm = new Vmm(initArgs);
                    _ = _vmm.GetMemoryMap(applyMap: true, outputFile: _memoryMapFile);
                }
                else
                {
                    if (useMemMap)
                        initArgs = [.. initArgs, "-memmap", _memoryMapFile];
                    _vmm = new Vmm(initArgs);
                }

                _vmm.RegisterAutoRefresh(RefreshOption.MemoryPartial, TimeSpan.FromMilliseconds(300));
                _vmm.RegisterAutoRefresh(RefreshOption.TlbPartial, TimeSpan.FromSeconds(2));

                GameStarted += Memory_GameStarted;
                GameStopped += Memory_GameStopped;
                RaidStarted += Memory_RaidStarted;
                RaidStopped += Memory_RaidStopped;

                new Thread(MemoryPrimaryWorker)
                {
                    IsBackground = true
                }.Start();

                Log.WriteLine("DMA Initialized!");
            }
            catch (Exception ex)
            {
                throw new Exception(
                    "DMA Initialization Failed!\n" +
                    $"Reason: {ex.Message}\n" +
                    $"Vmm Version: {vmmVersion}\n" +
                    $"Leechcore Version: {lcVersion}\n\n" +
                    "===TROUBLESHOOTING===\n" +
                    "1. Reboot both your Game PC / Radar PC (This USUALLY fixes it).\n" +
                    "2. Reseat all cables/connections and make sure they are secure.\n" +
                    "3. Changed Hardware/Operating System on Game PC? Delete your mmap.txt and symbols folder.\n" +
                    "4. Make sure all Setup Steps are completed (See DMA Setup Guide/FAQ for additional troubleshooting).");
            }
        }

        #endregion

        #region Worker Thread

        private static void MemoryPrimaryWorker()
        {
            Log.Write(AppLogLevel.Info, "Memory thread starting...");

            if (!MainWindow.Initialized)
            {
                Log.Write(AppLogLevel.Info, "Main window not ready, waiting...", "Waiting");
                while (!MainWindow.Initialized)
                    Thread.Sleep(100);
                Log.Write(AppLogLevel.Info, "Main window ready.", "Waiting");
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
                    Log.Write(AppLogLevel.Error, $"FATAL ERROR on Memory Thread: {ex}");
                    if (MainWindow.Window != null)
                        NotificationsShared.Warning("FATAL ERROR on Memory Thread");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Startup / Game Loop

        private static void RunStartupLoop()
        {
            Log.WriteLine("New Game Startup");
            var refreshCooldown = new Stopwatch();

            while (true)
            {
                try
                {
                    if (!refreshCooldown.IsRunning || refreshCooldown.ElapsedMilliseconds >= 3000)
                    {
                        FullRefresh();
                        refreshCooldown.Restart();
                    }
                    ResourceJanitor.Run();
                    LoadProcess();

                    // Try loading modules immediately — if the game was already
                    // running they will be available right away.  Only sleep and
                    // retry when the modules are not yet mapped (fresh launch).
                    bool modulesReady = false;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        try
                        {
                            if (attempt > 0)
                            {
                                FullRefresh();
                                refreshCooldown.Restart();
                            }

                            LoadModules();
                            modulesReady = true;
                            break;
                        }
                        catch
                        {
                            if (attempt == 0)
                                Log.WriteLine("[Startup] Process found, waiting for modules to load...");

                            Thread.Sleep(1000);
                        }
                    }

                    if (!modulesReady)
                        throw new Exception("Modules failed to load after retries");

                    _starting = true;

                    IL2CPP.Il2CppDumper.Dump();
                    CameraManager.Initialize();
                    InputManager.Initialize();
                    _ready = true;

                    Log.WriteLine("Game Startup [OK]");
                    if (MainWindow.Window != null)
                        NotificationsShared.Info("Game Startup [OK]");
                    return;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Game Startup [FAIL]: {ex}");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        private static void RunGameLoop()
        {
            while (true)
            {
                LocalGameWorld.RaidCooldown.WaitIfActive(_radarCts.Token);

                try
                {
                    var ct = _radarCts.Token;

                    using (var game = Game = LocalGameWorld.CreateGameInstance(ct))
                    {
                        _lastMapIdForMemWrites = game.MapID;
                        Log.WriteLine($"[Memory] New GameInstance created. Map = '{_lastMapIdForMemWrites}'");

                        OnRaidStarted();
                        game.Start();

                        while (game.InRaid)
                        {
                            ct.ThrowIfCancellationRequested();

                            var currentMapId = game.MapID;
                            if (!string.IsNullOrEmpty(currentMapId) &&
                                !string.IsNullOrEmpty(_lastMapIdForMemWrites) &&
                                !string.Equals(currentMapId, _lastMapIdForMemWrites, StringComparison.Ordinal))
                            {
                                Log.WriteLine(
                                    $"[Memory] Map transition detected: '{_lastMapIdForMemWrites}' -> '{currentMapId}'. " +
                                    "Resetting memwrite features / caches.");
                                OnRaidStopped();
                                OnRaidStarted();
                                _lastMapIdForMemWrites = currentMapId;
                            }

                            if (_restartRadar)
                            {
                                Log.WriteLine("Restarting Radar per User Request.");
                                if (MainWindow.Window != null)
                                    NotificationsShared.Info("Restarting Radar per User Request.");
                                _restartRadar = false;
                                LocalGameWorld.ClearStaleGuard();
                                RequestRestart();
                                break;
                            }

                            game.Refresh();
                            Thread.Sleep(133);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.WriteLine("Radar restart requested.");
                    continue;
                }
                catch (GameNotRunning)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"CRITICAL ERROR in Game Loop: {ex}");
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

            Log.WriteLine("Game is no longer running!");
            if (MainWindow.Window != null)
                NotificationsShared.Warning("Game is no longer running!");
        }

        #endregion

        #region Restart

        public static void RequestRestart()
        {
            lock (_restartSync)
            {
                var old = Interlocked.Exchange(ref _radarCts, new CancellationTokenSource());
                old.Cancel();
                old.Dispose();
            }
        }

        #endregion

        #region Process / Module Loading

        private static void LoadProcess()
        {
            if (!_vmm.PidGetFromName(_processName, out uint pid))
                throw new Exception($"Unable to find '{_processName}'");
            _pid = pid;
        }

        private static void LoadModules()
        {
            var unityBase = _vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
            ArgumentOutOfRangeException.ThrowIfZero(unityBase, nameof(unityBase));
            UnityBase = unityBase;

            var gameAssemblyBase = _vmm.ProcessGetModuleBase(_pid, "GameAssembly.dll");
            if (gameAssemblyBase != 0)
            {
                GameAssemblyBase = gameAssemblyBase;
                Log.WriteLine($"[IL2CPP] GameAssembly.dll base: 0x{gameAssemblyBase:X}");
            }
            else
            {
                Log.WriteLine("[IL2CPP] WARNING: GameAssembly.dll not found!");
            }

            GOM = IL2CPP.GameObjectManager.GetAddr(unityBase);
            ArgumentOutOfRangeException.ThrowIfZero(GOM, nameof(GOM));
            Log.WriteLine($"[IL2CPP] GOM Address: 0x{GOM:X}");
        }

        #endregion

        #region Events

        /// <summary>Raised when the game process starts.</summary>
        public static event EventHandler<EventArgs> GameStarted;
        /// <summary>Raised when the game process stops.</summary>
        public static event EventHandler<EventArgs> GameStopped;
        /// <summary>Raised when a raid starts.</summary>
        public static event EventHandler<EventArgs> RaidStarted;
        /// <summary>Raised when a raid ends.</summary>
        public static event EventHandler<EventArgs> RaidStopped;

        private static void OnGameStarted() => GameStarted?.Invoke(null, EventArgs.Empty);
        private static void OnGameStopped() => GameStopped?.Invoke(null, EventArgs.Empty);
        private static void OnRaidStarted() => RaidStarted?.Invoke(null, EventArgs.Empty);
        private static void OnRaidStopped() => RaidStopped?.Invoke(null, EventArgs.Empty);

        private static void Memory_GameStarted(object sender, EventArgs e)
        {
            _syncProcessRunning.Set();
        }

        private static void Memory_GameStopped(object sender, EventArgs e)
        {
            _restartRadar = default;
            _starting = default;
            _ready = default;
            UnityBase = default;
            GOM = default;
            _syncProcessRunning.Reset();
        }

        private static void Memory_RaidStarted(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            _syncInRaid.Set();
        }

        private static void Memory_RaidStopped(object sender, EventArgs e)
        {
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            _syncInRaid.Reset();
            Game = null;
            Program.Hideout.Reset();
        }

        /// <summary>Blocks until the game process is running.</summary>
        public static bool WaitForProcess() => _syncProcessRunning.WaitOne();
        /// <summary>Blocks until in a raid.</summary>
        public static bool WaitForRaid() => _syncInRaid.WaitOne();

        #endregion

        #region Scatter Read

        /// <summary>
        /// Performs multiple reads in one sequence using the native VmmScatter API.
        /// </summary>
        public static void ReadScatter(IScatterEntry[] entries, bool useCache = true)
            => ReadScatter(entries, entries.Length, useCache);

        public static void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0) return;

            var vmm = _vmm;
            if (vmm is null)
                throw new ObjectDisposedException(nameof(Memory));

            var vmmFlags = ToVmmFlags(useCache);
            using var scatter = new VmmScatter(vmm, _pid, vmmFlags);

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (entry.Address == 0x0 || entry.CB == 0 || (uint)entry.CB > MAX_READ_SIZE)
                {
                    entry.IsFailed = true;
                    continue;
                }
                if (!scatter.PrepareRead(entry.Address, (uint)entry.CB))
                    entry.IsFailed = true;
            }

            scatter.Execute();

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                if (!entry.IsFailed)
                    entry.ReadResult(scatter);
            }
        }

        #endregion

        #region Read Methods

        /// <summary>
        /// Read memory into a buffer of type <typeparamref name="T"/>.
        /// </summary>
        public static void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true)
            where T : unmanaged
        {
            var flags = ToVmmFlags(useCache);
            if (!_vmm.MemReadSpan(_pid, addr, buffer, flags))
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Read an array of type <typeparamref name="T"/> from memory.
        /// </summary>
        public static T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0) return [];
            T[] result = new T[count];
            ReadBuffer(addr, result.AsSpan(), useCache);
            return result;
        }

        /// <summary>
        /// Read raw bytes from memory.
        /// </summary>
        public static byte[] ReadBuffer(ulong addr, int size, bool useCache = true)
        {
            var flags = ToVmmFlags(useCache);
            var buf = _vmm.MemRead(_pid, addr, (uint)size, out uint cbRead, flags);
            if (cbRead != (uint)size)
                throw new VmmException($"Incomplete memory read at 0x{addr:X}");
            return buf ?? [];
        }

        /// <summary>
        /// Read a chain of pointers and get the final result.
        /// </summary>
        public static ulong ReadPtrChain(ulong addr, ReadOnlySpan<uint> offsets, bool useCache = true)
        {
            var pointer = addr;
            foreach (var offset in offsets)
                pointer = ReadPtr(pointer + offset, useCache);
            return pointer;
        }

        /// <summary>
        /// Resolves a pointer and returns the memory address it points to.
        /// </summary>
        public static ulong ReadPtr(ulong addr, bool useCache = true)
        {
            var pointer = ReadValue<ulong>(addr, useCache);
            pointer.ThrowIfInvalidVirtualAddress();
            return pointer;
        }

        /// <summary>
        /// Read value type/struct from specified address.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            var flags = ToVmmFlags(useCache);
            return _vmm.MemReadValue<T>(_pid, addr, flags);
        }

        /// <summary>
        /// Read value type/struct from specified address multiple times to ensure the read is correct.
        /// </summary>
        public static unsafe T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            T r1 = _vmm.MemReadValue<T>(_pid, addr, VmmFlags.NOCACHE);
            Thread.SpinWait(5);
            T r2 = _vmm.MemReadValue<T>(_pid, addr, VmmFlags.NOCACHE);
            Thread.SpinWait(5);
            T r3 = _vmm.MemReadValue<T>(_pid, addr, VmmFlags.NOCACHE);
            var b1 = new ReadOnlySpan<byte>(&r1, cb);
            var b2 = new ReadOnlySpan<byte>(&r2, cb);
            var b3 = new ReadOnlySpan<byte>(&r3, cb);
            if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3))
                throw new VmmException("Memory Read Failed!");
            return r1;
        }

        /// <summary>
        /// Read null terminated ASCII/UTF8 string.
        /// </summary>
        public static string ReadString(ulong addr, int cb = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            var flags = ToVmmFlags(useCache);
            return _vmm.MemReadString(_pid, addr, cb, Encoding.UTF8, flags) ??
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Read null terminated Unity string (Unicode Encoding).
        /// </summary>
        public static string ReadUnityString(ulong addr, int length = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x1000, nameof(length));
            var flags = ToVmmFlags(useCache);
            return _vmm.MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, flags) ??
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Reads the PE header TimeDateStamp and SizeOfImage from a remote module base address.
        /// Together these form a cheap version fingerprint that changes when a module is rebuilt.
        /// Returns (0, 0) on any read failure.
        /// </summary>
        public static (uint Timestamp, uint SizeOfImage) ReadPeFingerprint(ulong moduleBase)
        {
            try
            {
                uint eLfanew = ReadValue<uint>(moduleBase + 0x3C, false);
                if (eLfanew == 0 || eLfanew > 0x1000)
                    return (0, 0);

                uint timestamp = ReadValue<uint>(moduleBase + eLfanew + 8, false);
                uint sizeOfImage = ReadValue<uint>(moduleBase + eLfanew + 0x50, false);
                return (timestamp, sizeOfImage);
            }
            catch
            {
                return (0, 0);
            }
        }

        #endregion

        #region Signature Scanning

        /// <summary>
        /// Find a single signature match within a module.
        /// Falls back to chunked scanner for patterns exceeding VmmSharpEx's 32-byte limit.
        /// </summary>
        public static ulong FindSignature(string signature, string moduleName)
        {
            int tokenCount = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (tokenCount <= 32)
                return _vmm.FindSignature(_pid, signature, moduleName);

            var results = _vmm.FindSignatures(_pid, signature, moduleName, maxMatches: 1);
            return results.Length > 0 ? results[0] : 0;
        }

        /// <summary>
        /// Find multiple signature matches within a module (delegates to VmmSharpEx.Extensions).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong[] FindSignatures(string signature, string moduleName, int maxMatches = int.MaxValue)
        {
            return _vmm.FindSignatures(_pid, signature, moduleName, maxMatches);
        }

        #endregion

        #region Write Methods

        public static void WriteValue<T>(LocalGameWorld game, ulong addr, T value)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem) throw new InvalidOperationException("Not safe to write!");
            WriteValue(addr, value);
        }

        public static void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem) throw new InvalidOperationException("Not safe to write!");
            WriteBuffer(addr, buffer);
        }

        /// <summary>
        /// Write value to memory with verification (retries up to 3 times).
        /// </summary>
        public static unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            var b1 = new ReadOnlySpan<byte>(&value, cb);
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    WriteValue(addr, value);
                    Thread.SpinWait(5);
                    T temp = ReadValue<T>(addr, false);
                    var b2 = new ReadOnlySpan<byte>(&temp, cb);
                    if (b1.SequenceEqual(b2)) return;
                }
                catch { }
            }
            throw new VmmException("Memory Write Failed!");
        }

        /// <summary>
        /// Write value type to memory.
        /// </summary>
        public static unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!(SharedProgram.Config?.MemWritesEnabled ?? false))
                throw new InvalidOperationException("Memory Writing is Disabled!");
            Span<byte> buffer = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
            _vmm.MemWriteSpan(_pid, addr, buffer);
        }

        /// <summary>
        /// Write a buffer of type <typeparamref name="T"/> to memory.
        /// </summary>
        public static void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!(SharedProgram.Config?.MemWritesEnabled ?? false))
                throw new InvalidOperationException("Memory Writing is Disabled!");
            _vmm.MemWriteSpan(_pid, addr, buffer);
        }

        /// <summary>
        /// Write a buffer to memory with verification (retries up to 3 times).
        /// </summary>
        public static void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            int cb = SizeChecker<T>.Size * buffer.Length;
            Span<byte> temp = cb > 0x1000 ? new byte[cb] : stackalloc byte[cb];
            ReadOnlySpan<byte> b1 = MemoryMarshal.Cast<T, byte>(buffer);
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    WriteBuffer(addr, buffer);
                    Thread.SpinWait(5);
                    temp.Clear();
                    ReadBuffer(addr, temp, false);
                    if (temp.SequenceEqual(b1)) return;
                }
                catch { }
            }
            throw new VmmException("Memory Write Failed!");
        }

        #endregion

        #region Misc

        public static void FullRefresh()
        {
            _vmm?.ForceFullRefresh();
        }

        /// <summary>
        /// Throws a special exception if the game process is no longer running.
        /// </summary>
        /// <exception cref="GameNotRunning"></exception>
        public static void ThrowIfNotInGame()
        {
            FullRefresh();
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    if (_vmm.PidGetFromName(_processName, out uint pid) && pid == _pid)
                        return;
                }
                catch { Thread.Sleep(150); }
            }
            throw new GameNotRunning();
        }

        public static Rectangle? GetMonitorRes()
        {
            try
            {
                var gfx = ReadPtr(UnityBase + UnityOffsets.ModuleBase.GfxDevice, false);
                var res = ReadValue<Rectangle>(gfx + UnityOffsets.GfxDeviceClient.Viewport, false);
                if (res.Width <= 0 || res.Width > 10000 || res.Height <= 0 || res.Height > 5000)
                    return null;
                return res;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Memory] ERROR Getting Game Monitor Res: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a new <see cref="VmmScatter"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter GetScatter(VmmFlags flags) =>
            new VmmScatter(_vmm, _pid, flags);

        /// <summary>
        /// Close the FPGA connection.
        /// </summary>
        public static void Close()
        {
            _vmm?.Dispose();
            _vmm = null;
        }

        public sealed class GameNotRunning : Exception
        {
            public GameNotRunning() { }
            public GameNotRunning(string message) : base(message) { }
            public GameNotRunning(string message, Exception inner) : base(message, inner) { }
        }

        #endregion
    }
}
