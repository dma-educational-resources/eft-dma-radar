global using eft_dma_radar.DMA;

using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Hideout;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;
using IL2CPP = eft_dma_radar.Tarkov.Unity.IL2CPP;

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
        public static bool RaidHasStarted => Game?.RaidHasStarted ?? false;
        public static bool IsOffline => LocalGameWorld.IsOffline;
        public static ulong LevelSettings => LocalGameWorld.LevelSettings;

        public static IReadOnlyCollection<Player> Players => Game?.Players;
        public static IReadOnlyCollection<IExplosiveItem> Explosives => Game?.Explosives;
        public static IReadOnlyCollection<IExitPoint> Exits => Game?.Exits;
        public static LocalPlayer LocalPlayer => Game?.LocalPlayer;
        public static LootManager Loot => Game?.Loot;
        public static QuestManager QuestManager => Game?.QuestManager;
        public static LocalGameWorld Game { get; private set; }
        public static HideoutManager Hideout { get; } = new();

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

                    Log.WriteLine("[Startup] Process found, waiting for modules to load...");
                    Thread.Sleep(5000);
                    FullRefresh();
                    refreshCooldown.Restart();

                    LoadModules();
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
            Hideout.Reset();
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

            var vmmFlags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            using var scatter = new VmmScatter(_vmm, _pid, vmmFlags);

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
        /// Prefetch pages into the cache.
        /// </summary>
        public static void ReadCache(params ulong[] va)
        {
            _vmm.MemPrefetchPages(_pid, va.AsSpan());
        }

        /// <summary>
        /// Read memory into a buffer of type <typeparamref name="T"/>.
        /// </summary>
        public static void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged
        {
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            if (!_vmm.MemReadSpan(_pid, addr, buffer, flags))
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Read an array of type <typeparamref name="T"/> from memory.
        /// </summary>
        public static T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0) return Array.Empty<T>();
            T[] result = new T[count];
            ReadBuffer(addr, result.AsSpan(), useCache);
            return result;
        }

        /// <summary>
        /// Read an array of type <typeparamref name="T"/> from memory into a pooled buffer.
        /// IMPORTANT: Caller must call <see cref="IDisposable.Dispose"/> on the returned value.
        /// </summary>
        public static IMemoryOwner<T> ReadPooled<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            return _vmm.MemReadPooled<T>(_pid, addr, count, flags) ??
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Read raw bytes from memory.
        /// </summary>
        public static byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            try
            {
                var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
                var buf = _vmm.MemRead(_pid, addr, (uint)size, out uint cbRead, flags);
                if (!allowIncompleteRead && cbRead != (uint)size)
                    throw new Exception("Incomplete memory read!");
                return buf ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }

        /// <summary>
        /// Read a chain of pointers and get the final result.
        /// </summary>
        public static ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            var pointer = addr;
            for (var i = 0; i < offsets.Length; i++)
                pointer = ReadPtr(pointer + offsets[i], useCache);
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
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            return _vmm.MemReadValue<T>(_pid, addr, flags);
        }

        /// <summary>
        /// Read value type/struct from specified address (out parameter variant).
        /// </summary>
        public static void ReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            result = _vmm.MemReadValue<T>(_pid, addr, flags);
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
            if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3) || !b2.SequenceEqual(b3))
                throw new VmmException("Memory Read Failed!");
            return r1;
        }

        /// <summary>
        /// Read value type/struct from specified address multiple times to ensure the read is correct (out parameter variant).
        /// </summary>
        public static unsafe void ReadValueEnsure<T>(ulong addr, out T result)
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
            result = r1;
        }

        public static bool TryReadValueEnsure<T>(ulong addr, out T result) where T : unmanaged
        {
            try { ReadValueEnsure(addr, out result); return true; }
            catch { result = default; return false; }
        }

        /// <summary>
        /// Read null terminated ASCII/UTF8 string.
        /// </summary>
        public static string ReadString(ulong addr, int cb = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            var flags = useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;
            return _vmm.MemReadString(_pid, addr, cb, Encoding.UTF8, flags) ??
                throw new VmmException("Memory Read Failed!");
        }

        /// <summary>
        /// Read null terminated Unity string (Unicode Encoding).
        /// </summary>
        public static string ReadUnityString(ulong addr, int length = 64, bool useCache = true)
        {
            if (length % 2 != 0) length++;
            length *= 2;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (int)0x1000, nameof(length));
            Span<byte> buffer = stackalloc byte[length];
            buffer.Clear();
            ReadBuffer(addr + 0x14, buffer, useCache, true);
            var nullIndex = buffer.FindUtf16NullTerminatorIndex();
            return nullIndex >= 0
                ? Encoding.Unicode.GetString(buffer[..nullIndex])
                : Encoding.Unicode.GetString(buffer);
        }

        #endregion

        #region Signature Scanning

        /// <summary>
        /// Find a single signature match within a module using VmmSharpEx built-in scanner.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong FindSignature(string signature, string moduleName)
        {
            return _vmm.FindSignature(_pid, signature, moduleName);
        }

        /// <summary>
        /// Find multiple signature matches within a module (custom chunked scanner).
        /// </summary>
        public static ulong[] FindSignatures(string signature, string moduleName, int maxMatches = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(signature) || maxMatches <= 0)
                return Array.Empty<ulong>();
            if (!TryParseSignature(signature, out var pattern))
                return Array.Empty<ulong>();

            try
            {
                var moduleBase = _vmm.ProcessGetModuleBase(_pid, moduleName);
                if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                {
                    Log.WriteLine($"[Signature] Module {moduleName} not found");
                    return Array.Empty<ulong>();
                }

                const ulong MAX_SEARCH_SIZE = 0xC800000;
                const ulong CHUNK_SIZE = 0x1000000;
                ulong rangeEnd = moduleBase + MAX_SEARCH_SIZE;
                int overlap = Math.Max(0x100, pattern.Length - 1);
                ulong step = CHUNK_SIZE > (ulong)overlap ? CHUNK_SIZE - (ulong)overlap : CHUNK_SIZE;
                var results = new List<ulong>(Math.Min(maxMatches, 64));

                for (ulong chunkStart = moduleBase; chunkStart < rangeEnd && results.Count < maxMatches; chunkStart += step)
                {
                    ulong chunkEnd = Math.Min(chunkStart + CHUNK_SIZE, rangeEnd);
                    var chunkMatches = FindSignaturesInRange(pattern, chunkStart, chunkEnd, _pid, maxMatches - results.Count);
                    foreach (var match in chunkMatches)
                    {
                        if (results.Count == 0 || results[^1] != match) results.Add(match);
                        if (results.Count >= maxMatches) break;
                    }
                }
                return results.ToArray();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Signature] Error searching module {moduleName}: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        private static ulong[] FindSignaturesInRange(byte?[] pattern, ulong rangeStart, ulong rangeEnd, uint pid, int maxMatches)
        {
            if (pattern.Length == 0 || rangeStart >= rangeEnd || maxMatches <= 0)
                return Array.Empty<ulong>();
            try
            {
                byte[] buffer = _vmm.MemRead(pid, rangeStart, (uint)(rangeEnd - rangeStart), out _, VmmFlags.NOCACHE);
                if (buffer is null || buffer.Length < pattern.Length)
                    return Array.Empty<ulong>();

                var matches = new List<ulong>(Math.Min(maxMatches, 32));
                int lastStart = buffer.Length - pattern.Length;
                for (int i = 0; i <= lastStart; i++)
                {
                    bool isMatch = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        var expected = pattern[j];
                        if (expected.HasValue && buffer[i + j] != expected.Value) { isMatch = false; break; }
                    }
                    if (!isMatch) continue;
                    matches.Add(rangeStart + (ulong)i);
                    if (matches.Count >= maxMatches) break;
                }
                return matches.ToArray();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DMA] Error in FindSignatures: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        private static bool TryParseSignature(string signature, out byte?[] pattern)
        {
            var parts = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) { pattern = Array.Empty<byte?>(); return false; }
            pattern = new byte?[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part is "?" or "??") { pattern[i] = null; continue; }
                if (part.Length != 2 || !byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out var b))
                { pattern = Array.Empty<byte?>(); return false; }
                pattern[i] = b;
            }
            return true;
        }

        #endregion

        #region Write Methods

        public static void WriteValue<T>(LocalGameWorld game, ulong addr, T value)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem) throw new Exception("Not safe to write!");
            WriteValue(addr, value);
        }

        public static void WriteBuffer<T>(LocalGameWorld game, ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!game.IsSafeToWriteMem) throw new Exception("Not safe to write!");
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
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
            int size = sizeof(T);
            Span<byte> buffer = stackalloc byte[size];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer), value);
            _vmm.MemWriteSpan(_pid, addr, buffer);
        }

        /// <summary>
        /// Write a buffer of type <typeparamref name="T"/> to memory.
        /// </summary>
        public static void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
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
                    ReadBuffer(addr, temp, false, false);
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

        public static Rectangle GetMonitorRes()
        {
            try
            {
                var gfx = ReadPtr(UnityBase + UnityOffsets.ModuleBase.GfxDevice, false);
                var res = ReadValue<Rectangle>(gfx + UnityOffsets.GfxDeviceClient.Viewport, false);
                if (res.Width <= 0 || res.Width > 10000 || res.Height <= 0 || res.Height > 5000)
                    throw new ArgumentOutOfRangeException(nameof(res));
                return res;
            }
            catch (Exception ex)
            {
                throw new Exception("ERROR Getting Game Monitor Res", ex);
            }
        }

        public static bool TryReadValue<T>(ulong addr, out T value) where T : unmanaged
        {
            try { value = ReadValue<T>(addr); return true; }
            catch { value = default; return false; }
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
