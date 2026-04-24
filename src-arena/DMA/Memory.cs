using System.IO;
using System.Runtime;
using eft_dma_radar.Arena.DMA.ScatterAPI;
using eft_dma_radar.Arena.GameWorld;
using eft_dma_radar.Arena.Unity;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;
using GameObjectManager = eft_dma_radar.Arena.Unity.GOM;

namespace eft_dma_radar.Arena.DMA
{
    internal static class Memory
    {
        #region Constants / Fields

        private const string ProcessName = "EscapeFromTarkovArena.exe";
        private const string MemMapFile  = "mmap.txt";
        public const uint MAX_READ_SIZE  = 0x1000u * 1500u;

        private static Vmm? _vmm;
        private static uint _pid;
        private static readonly Lock _restartLock = new();
        private static CancellationTokenSource _cts = new();
        private static volatile bool _shutdown;
        private static Thread? _workerThread;
        private static MemoryState _state = MemoryState.NotStarted;

        public static Action<string, NotificationLevel>? ShowNotification;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VmmFlags ToFlags(bool useCache) =>
            useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vmm VmmOrThrow() =>
            _vmm ?? throw new ObjectDisposedException(nameof(Memory));

        #endregion

        #region Public State

        public static MemoryState State => _state;
        internal static Vmm? VmmHandle => _vmm;
        public static ulong UnityBase          { get; private set; }
        public static ulong GOM                { get; private set; }
        public static ulong GameAssemblyBase   { get; private set; }
        public static bool Ready  => _state is MemoryState.ProcessFound or MemoryState.InGame;
        public static bool InGame => _state is MemoryState.InGame;

        /// <summary>Current live game world, or null if not in a match.</summary>
        public static LocalGameWorld? CurrentGameWorld => _currentGameWorld;
        private static LocalGameWorld? _currentGameWorld;

        #endregion

        #region Events

        public static event EventHandler<EventArgs>? GameStarted;
        public static event EventHandler<EventArgs>? GameStopped;

        private static void OnGameStarted()
        {
            SetState(MemoryState.ProcessFound);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        private static void OnGameStopped()
        {
            SetState(MemoryState.WaitingForProcess);
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            UnityBase        = default;
            GOM              = default;
            GameAssemblyBase = default;
            _pid             = default;
            GameObjectManager.ResetCachedAddresses();
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        private static void SetState(MemoryState s)
        {
            _state = s;
            Log.WriteLine($"[Memory] State → {s}");
        }

        #endregion

        #region Init

        public static void ModuleInit(ArenaConfig config)
        {
            Log.WriteLine("[Memory] Initializing DMA...");

            var vmmVer = FileVersionInfo.GetVersionInfo("vmm.dll").FileVersion;
            var lcVer  = FileVersionInfo.GetVersionInfo("leechcore.dll").FileVersion;

            var args = new List<string>(["-norefresh", "-device", config.DeviceStr, "-waitinitialize"]);

            try
            {
                if (config.MemMapEnabled && !File.Exists(MemMapFile))
                {
                    Log.WriteLine("[Memory] No MemMap, generating...");
                    _vmm = new Vmm([.. args]);
                    _vmm.GetMemoryMap(applyMap: true, outputFile: MemMapFile);
                    _vmm.Dispose();
                }

                if (config.MemMapEnabled)
                    args.AddRange(["-memmap", MemMapFile]);

                _vmm = new Vmm([.. args]);
                _vmm.RegisterAutoRefresh(RefreshOption.MemoryPartial, TimeSpan.FromMilliseconds(300));
                _vmm.RegisterAutoRefresh(RefreshOption.TlbPartial,    TimeSpan.FromSeconds(2));

                SetState(MemoryState.WaitingForProcess);

                _workerThread = new Thread(MemoryWorker) { IsBackground = true, Name = "MemoryWorker" };
                _workerThread.Start();

                Log.WriteLine("[Memory] DMA initialized OK.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"DMA Initialization Failed!\n" +
                    $"Reason: {ex.Message}\n" +
                    $"vmm: {vmmVer}  leechcore: {lcVer}\n\n" +
                    "Troubleshooting:\n" +
                    "1. Reboot both PCs.\n" +
                    "2. Check all cable connections.\n" +
                    "3. Changed hardware? Delete mmap.txt.\n" +
                    "4. Verify all DMA setup steps are complete.", ex);
            }
        }

        #endregion

        #region Worker

        private static void MemoryWorker()
        {
            Log.WriteLine("[Memory] Worker thread started.");
            while (!_shutdown)
            {
                try
                {
                    RunStartupLoop();
                    if (_shutdown) break;
                    OnGameStarted();
                    RunGameLoop();
                    OnGameStopped();
                }
                catch (OperationCanceledException) when (_shutdown) { break; }
                catch (ObjectDisposedException)                      { break; }
                catch (Exception ex)
                {
                    if (_shutdown) break;
                    Log.Write(AppLogLevel.Error, $"FATAL on memory thread: {ex}");
                    Notify("FATAL error on memory thread — restarting", NotificationLevel.Error);
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
            Log.WriteLine("[Memory] Worker thread exiting.");
        }

        #endregion

        #region Startup Loop

        private static void RunStartupLoop()
        {
            Log.WriteLine("[Memory] Waiting for Arena game process...");
            SetState(MemoryState.WaitingForProcess);
            var cooldown = Stopwatch.StartNew();

            while (!_shutdown)
            {
                try
                {
                    if (cooldown.ElapsedMilliseconds >= 3000)
                    {
                        FullRefresh();
                        cooldown.Restart();
                    }

                    LoadProcess();

                    bool modulesReady = false;
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            if (i > 0) { FullRefresh(); cooldown.Restart(); }
                            LoadModules();
                            modulesReady = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (i == 0) Log.WriteLine("[Memory] Process found, waiting for modules...");
                            Log.WriteLine($"[Memory] Module load attempt {i + 1}/10 failed: {ex.Message}");
                            Thread.Sleep(1000);
                        }
                    }

                    if (!modulesReady)
                        throw new Exception("Modules failed to load after 10 retries.");

                    WaitForTypeInfoTable();

                    eft_dma_radar.Arena.Unity.IL2CPP.Il2CppDumper.Dump();
                    //eft_dma_radar.Arena.Unity.IL2CPP.Il2CppDumper.DumpAll();

                    // Pre-warm CameraManager: sig-scan AllCameras + Camera struct offsets
                    // before the first match starts. Matches EFT's startup model so the camera
                    // worker can create instantly on match start instead of retrying for seconds.
                    eft_dma_radar.Arena.GameWorld.CameraManager.Initialize();

                    SetState(MemoryState.Initializing);
                    Log.WriteLine("[Memory] Game startup OK.");
                    Notify("Arena game startup OK", NotificationLevel.Info);
                    return;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[Memory] Startup failed: {ex.Message}");
                    OnGameStopped();
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Game Loop

        private static void RunGameLoop()
        {
            while (!_shutdown)
            {
                LocalGameWorld? gameWorld = null;
                try
                {
                    var ct = _cts.Token;

                    Log.WriteLine("[Memory] Searching for Arena match...");
                    gameWorld = LocalGameWorld.Create(ct);
                    _currentGameWorld = gameWorld;
                    SetState(MemoryState.InGame);
                    gameWorld.Start();

                    while (!_shutdown)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!gameWorld.InMatch) break;
                        Thread.Sleep(200);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (_shutdown) break;
                    continue;
                }
                catch (ObjectDisposedException)  { break; }
                catch (Memory.GameNotRunningException)  { break; }
                catch (Exception ex)
                {
                    if (_shutdown) break;
                    Log.WriteLine($"[Memory] CRITICAL in game loop: {ex}");
                    Notify("CRITICAL error in game loop", NotificationLevel.Error);
                    break;
                }
                finally
                {
                    _currentGameWorld = null;
                    gameWorld?.Dispose();
                    if (_state == MemoryState.InGame)
                        SetState(MemoryState.ProcessFound);
                }
            }

            if (!_shutdown)
            {
                Log.WriteLine("[Memory] Game is no longer running.");
                Notify("Game is no longer running", NotificationLevel.Warning);
            }
        }

        #endregion

        #region Restart

        public static void RequestRestart()
        {
            lock (_restartLock)
            {
                var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
                old.Cancel();
                old.Dispose();
            }
        }

        #endregion

        #region Process / Module Loading

        private static void LoadProcess()
        {
            if (!VmmOrThrow().PidGetFromName(ProcessName, out uint pid))
                throw new Exception($"Process '{ProcessName}' not found.");
            _pid = pid;
        }

        private static void LoadModules()
        {
            var vmm = VmmOrThrow();
            var unityBase = vmm.ProcessGetModuleBase(_pid, "UnityPlayer.dll");
            ArgumentOutOfRangeException.ThrowIfZero(unityBase, nameof(unityBase));
            UnityBase = unityBase;

            var gaBase = vmm.ProcessGetModuleBase(_pid, "GameAssembly.dll");
            if (gaBase != 0)
            {
                GameAssemblyBase = gaBase;
                Log.WriteLine($"[Memory] GameAssembly.dll base: 0x{gaBase:X}");
            }
            else
            {
                Log.WriteLine("[Memory] WARNING: GameAssembly.dll not found.");
            }

            GOM = GameObjectManager.GetAddr(unityBase);
            ArgumentOutOfRangeException.ThrowIfZero(GOM, nameof(GOM));
            Log.WriteLine($"[Memory] GOM: 0x{GOM:X}");
        }

        private static void WaitForTypeInfoTable()
        {
            var gaBase = GameAssemblyBase;
            if (gaBase == 0) return;

            var rva = SDK.Offsets.Special.TypeInfoTableRva;
            if (rva == 0)
            {
                Log.WriteLine("[Memory] TypeInfoTableRva is 0 — skipping pre-dump wait.");
                return;
            }

            const int timeoutMs  = 60_000;
            const int intervalMs = 500;
            var sw = Stopwatch.StartNew();
            bool logged = false;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var tablePtr = ReadValue<ulong>(gaBase + rva, false);
                    if (tablePtr.IsValidVirtualAddress())
                    {
                        if (logged)
                            Log.WriteLine($"[Memory] TypeInfoTable ready (waited {sw.ElapsedMilliseconds}ms).");
                        return;
                    }
                }
                catch { }

                if (!logged)
                {
                    Log.WriteLine("[Memory] Waiting for IL2CPP TypeInfoTable to initialize...");
                    logged = true;
                }

                Thread.Sleep(intervalMs);
            }

            Log.WriteLine("[Memory] TypeInfoTable wait timed out — proceeding; Il2CppDumper retry loop will handle it.");
        }

        #endregion

        #region Scatter Read

        public static void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0) return;
            using var scatter = new VmmScatter(VmmOrThrow(), _pid, ToFlags(useCache));

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                if (!e.Address.IsValidVirtualAddress() || e.CB == 0 || (uint)e.CB > MAX_READ_SIZE)
                {
                    e.IsFailed = true;
                    continue;
                }
                if (!scatter.PrepareRead(e.Address, (uint)e.CB))
                    e.IsFailed = true;
            }

            scatter.Execute();

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                if (!e.IsFailed)
                    e.ReadResult(scatter);
            }
        }

        public static void ReadScatter(IScatterEntry[] entries, bool useCache = true)
            => ReadScatter(entries, entries.Length, useCache);

        #endregion

        #region Read Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            return VmmOrThrow().MemReadValue<T>(_pid, addr, ToFlags(useCache));
        }

        public static ulong ReadPtr(ulong addr, bool useCache = true)
        {
            var ptr = ReadValue<ulong>(addr, useCache);
            if (!ptr.IsValidVirtualAddress())
                throw new BadPtrException(addr, ptr);
            return ptr;
        }

        public static ulong ReadPtrChain(ulong addr, ReadOnlySpan<uint> offsets, bool useCache = true)
        {
            var p = addr;
            foreach (var o in offsets)
                p = ReadPtr(p + o, useCache);
            return p;
        }

        public static void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            if (buffer.IsEmpty) return;
            if (!VmmOrThrow().MemReadSpan(_pid, addr, buffer, ToFlags(useCache)))
                throw new VmmException("Memory read failed.");
        }

        public static T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0) return [];
            T[] arr = new T[count];
            ReadBuffer(addr, arr.AsSpan(), useCache);
            return arr;
        }

        public static string ReadString(ulong addr, int cb = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cb, 0, nameof(cb));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            return VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache))
                ?? throw new VmmException("String read failed.");
        }

        public static string ReadUnityString(ulong addr, int length = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0, nameof(length));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x1000, nameof(length));
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            return VmmOrThrow().MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, ToFlags(useCache))
                ?? throw new VmmException("Unity string read failed.");
        }

        #endregion

        #region Try Read Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress()) { result = default; return false; }
            return VmmOrThrow().MemReadValue(_pid, addr, out result, ToFlags(useCache));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadPtr(ulong addr, out ulong result, bool useCache = true)
        {
            if (!TryReadValue(addr, out result, useCache)) return false;
            return result.IsValidVirtualAddress();
        }

        public static bool TryReadPtrChain(ulong addr, ReadOnlySpan<uint> offsets, out ulong result, bool useCache = true)
        {
            result = addr;
            foreach (var o in offsets)
                if (!TryReadPtr(result + o, out result, useCache)) return false;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress()) return false;
            if (buffer.IsEmpty) return true;
            return VmmOrThrow().MemReadSpan(_pid, addr, buffer, ToFlags(useCache));
        }

        public static bool TryReadString(ulong addr, out string? result, int cb = 128, bool useCache = true)
        {
            result = null;
            if (cb <= 0 || cb > 0x1000) return false;
            if (!addr.IsValidVirtualAddress()) return false;
            result = VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache));
            return result is not null;
        }

        public static bool TryReadUnityString(ulong addr, out string? result, int length = 128, bool useCache = true)
        {
            result = null;
            if (length <= 0 || length > 0x1000) return false;
            if (!addr.IsValidVirtualAddress()) return false;
            result = VmmOrThrow().MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, ToFlags(useCache));
            return result is not null;
        }

        public static (uint Timestamp, uint SizeOfImage) ReadPeFingerprint(ulong moduleBase)
        {
            if (!TryReadValue<uint>(moduleBase + 0x3C, out var eLfanew, false) || eLfanew == 0 || eLfanew > 0x1000)
                return (0, 0);
            if (!TryReadValue<uint>(moduleBase + eLfanew + 8, out var ts, false)) return (0, 0);
            if (!TryReadValue<uint>(moduleBase + eLfanew + 0x50, out var sz, false)) return (0, 0);
            return (ts, sz);
        }

        #endregion

        #region Signature Scanning

        public static ulong FindSignature(string signature, string moduleName)
        {
            var vmm = VmmOrThrow();
            int tokens = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (tokens <= 32)
                return vmm.FindSignature(_pid, signature, moduleName);
            var results = vmm.FindSignatures(_pid, signature, moduleName, maxMatches: 1);
            return results.Length > 0 ? results[0] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong[] FindSignatures(string signature, string moduleName, int maxMatches = int.MaxValue)
            => VmmOrThrow().FindSignatures(_pid, signature, moduleName, maxMatches);

        #endregion

        #region Write Methods

        public static unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            Span<byte> buf = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buf), value);
            VmmOrThrow().MemWriteSpan(_pid, addr, buf);
        }

        public static void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!addr.IsValidVirtualAddress())
                throw new BadPtrException(0, addr);
            VmmOrThrow().MemWriteSpan(_pid, addr, buffer);
        }

        #endregion

        #region Misc

        public static void FullRefresh() => _vmm?.ForceFullRefresh();

        public static void ThrowIfNotInGame()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    FullRefresh();
                    if (VmmOrThrow().PidGetFromName(ProcessName, out uint pid) && pid == _pid)
                        return;
                }
                catch { Thread.Sleep(150); }
            }
            throw new GameNotRunningException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter GetScatter(VmmFlags flags) => new(VmmOrThrow(), _pid, flags);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter CreateScatter(bool useCache = true) => new(VmmOrThrow(), _pid, ToFlags(useCache));

        public static void Close()
        {
            if (_shutdown) return;
            _shutdown = true;
            try { _cts.Cancel(); } catch { }
            _workerThread?.Join(TimeSpan.FromSeconds(5));
            _vmm?.Dispose();
            _vmm = null;
            Log.WriteLine("[Memory] Closed.");
        }

        private static void Notify(string msg, NotificationLevel level)
        {
            try { ShowNotification?.Invoke(msg, level); }
            catch { }
        }

        #endregion

        #region Nested Types

        public sealed class GameNotRunningException : DmaException
        {
            public GameNotRunningException()
                : base("Arena game process is no longer running.") { }
        }

        #endregion
    }

    public enum MemoryState
    {
        NotStarted,
        WaitingForProcess,
        Initializing,
        ProcessFound,
        InGame,
    }

    public enum NotificationLevel { Info, Warning, Error }
}
