using System.IO;
using System.Runtime;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Silk.Tarkov.Hideout;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using GameObjectManager = eft_dma_radar.Silk.Tarkov.Unity.GOM;
using VmmSharpEx;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.DMA
{
    /// <summary>
    /// Standalone DMA Memory module for the Silk.NET radar.
    /// Minimal dependencies, self-contained.
    /// </summary>
    internal static class Memory
    {
        #region Constants / Fields

        private const string ProcessName = "EscapeFromTarkov.exe";
        private const string MemMapFile = "mmap.txt";
        public const uint MAX_READ_SIZE = 0x1000u * 1500u;

        private static Vmm? _vmm;
        private static uint _pid;
        private static readonly Lock _restartLock = new();
        private static CancellationTokenSource _cts = new();
        private static volatile bool _shutdown;
        private static Thread? _workerThread;

        private static MemoryState _state = MemoryState.NotStarted;

        /// <summary>
        /// Optional UI notification callback. Set by RadarWindow after init.
        /// Thread-safe: invoked from the memory worker thread.
        /// </summary>
        public static Action<string, NotificationLevel>? ShowNotification;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static VmmFlags ToFlags(bool useCache) =>
            useCache ? VmmFlags.NONE : VmmFlags.NOCACHE;

        /// <summary>Returns the active VMM handle or throws if disposed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vmm VmmOrThrow() =>
            _vmm ?? throw new ObjectDisposedException(nameof(Memory));

        #endregion

        #region Public State

        public static MemoryState State => _state;
        /// <summary>The active VMM handle, or <see langword="null"/> if not yet initialized.</summary>
        internal static Vmm? VmmHandle => _vmm;
        public static ulong UnityBase { get; private set; }
        public static ulong GOM { get; private set; }
        public static ulong GameAssemblyBase { get; private set; }
        public static bool Ready => _state is MemoryState.ProcessFound or MemoryState.InRaid or MemoryState.InHideout;
        public static bool InRaid => _state is MemoryState.InRaid;
        public static bool InHideout => _state is MemoryState.InHideout;

        /// <summary>Hideout manager instance — persists across hideout entries.</summary>
        public static HideoutManager Hideout { get; } = new();

        /// <summary>Persistent player history — tracks PMCs seen across sessions.</summary>
        public static PlayerHistory PlayerHistory { get; } = new();

        /// <summary>Manual player watchlist — persists user-tagged players across sessions.</summary>
        public static PlayerWatchlist PlayerWatchlist { get; } = new();

        public static LocalGameWorld? Game { get; private set; }
        public static string? MapID => Game?.MapID;
        public static RegisteredPlayers? Players => Game?.RegisteredPlayers;
        public static Player? LocalPlayer => Game?.LocalPlayer;
        public static IReadOnlyList<LootItem>? Loot => Game?.Loot;
        public static IReadOnlyList<LootCorpse>? Corpses => Game?.Corpses;
        public static IReadOnlyList<LootContainer>? Containers => Game?.Containers;
        public static IReadOnlyList<LootAirdrop>? Airdrops => Game?.Airdrops;
        public static IReadOnlyList<Exfil>? Exfils => Game?.Exfils;
        public static IReadOnlyList<TransitPoint>? Transits => Game?.Transits;
        public static IReadOnlyList<Door>? Doors => Game?.Doors;
        public static IReadOnlyList<QuestLocation>? QuestLocations => Game?.QuestLocations;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Quests.QuestManager? QuestManager => Game?.QuestManager ?? LobbyQuestReader.QuestManager;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Profile.WishlistManager? WishlistManager => Game?.WishlistManager;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Explosives.ExplosivesManager? Explosives => Game?.Explosives;
        public static eft_dma_radar.Silk.Tarkov.GameWorld.Btr.BtrTracker? Btr => Game?.Btr;
        public static IReadOnlyList<eft_dma_radar.Silk.Tarkov.GameWorld.Interactables.Switch>? Switches => Game?.Switches;

        #endregion

        #region Events

        /// <summary>Raised when the game process is found and ready.</summary>
        public static event EventHandler<EventArgs>? GameStarted;
        /// <summary>Raised when the game process is no longer running.</summary>
        public static event EventHandler<EventArgs>? GameStopped;
        /// <summary>Raised when a raid begins.</summary>
        public static event EventHandler<EventArgs>? RaidStarted;
        /// <summary>Raised when a raid ends.</summary>
        public static event EventHandler<EventArgs>? RaidStopped;
        /// <summary>Raised when the player enters the hideout.</summary>
        public static event EventHandler<EventArgs>? HideoutEntered;
        /// <summary>Raised when the player leaves the hideout.</summary>
        public static event EventHandler<EventArgs>? HideoutExited;

        private static void OnGameStarted()
        {
            SetState(MemoryState.ProcessFound);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            LobbyQuestReader.Start();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.Start();
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        private static void OnGameStopped()
        {
            SetState(MemoryState.WaitingForProcess);
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            UnityBase = default;
            GOM = default;
            GameAssemblyBase = default;
            _pid = default;
            GameObjectManager.ResetCachedAddresses();
            MatchingProgressResolver.Reset();
            Hideout.Reset();
            KillfeedManager.Reset();
            LobbyQuestReader.InvalidateCache();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.InvalidateCache();
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        private static void OnRaidStarted()
        {
            SetState(MemoryState.InRaid);
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            RaidStarted?.Invoke(null, EventArgs.Empty);
        }

        private static void OnRaidStopped()
        {
            if (_state is MemoryState.InRaid or MemoryState.InHideout)
                SetState(MemoryState.ProcessFound);
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            Game = null;
            LootFilter.ClearCaches();
            RaidStopped?.Invoke(null, EventArgs.Empty);
        }

        private static void OnHideoutEntered()
        {
            SetState(MemoryState.InHideout);
            HideoutEntered?.Invoke(null, EventArgs.Empty);
        }

        private static void OnHideoutExited()
        {
            if (_state is MemoryState.InHideout)
                SetState(MemoryState.ProcessFound);
            Game = null;
            Hideout.InvalidatePointers();
            HideoutExited?.Invoke(null, EventArgs.Empty);
        }

        private static void SetState(MemoryState s)
        {
            _state = s;
            Log.WriteLine($"[Memory] State → {s}");
        }

        #endregion

        #region Init

        /// <summary>
        /// Initialize the DMA layer. Called once from <see cref="SilkProgram.Main"/>.
        /// </summary>
        public static void ModuleInit(SilkConfig config)
        {
            Log.WriteLine("[Memory] Initializing DMA...");

            var vmmVer = FileVersionInfo.GetVersionInfo("vmm.dll").FileVersion;
            var lcVer = FileVersionInfo.GetVersionInfo("leechcore.dll").FileVersion;

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
                _vmm.RegisterAutoRefresh(RefreshOption.TlbPartial, TimeSpan.FromSeconds(2));

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
                    "3. Changed hardware? Delete mmap.txt and the Symbols folder.\n" +
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
                catch (OperationCanceledException) when (_shutdown)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
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
            Log.WriteLine("[Memory] Waiting for game process...");
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

                    // Retry module loading — game may still be initializing
                    bool modulesReady = false;
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            if (i > 0)
                            {
                                FullRefresh();
                                cooldown.Restart();
                            }
                            LoadModules();
                            modulesReady = true;
                            break;
                        }
                        catch
                        {
                            if (i == 0)
                                Log.WriteLine("[Memory] Process found, waiting for modules...");
                            Thread.Sleep(1000);
                        }
                    }

                    if (!modulesReady)
                        throw new Exception("Modules failed to load after retries.");

                    // Wait for EFT's IL2CPP runtime to finish initializing the TypeInfoTable.
                    // The game populates it a few seconds after its modules load; probing it
                    // directly is the most reliable guard.  We check using the hardcoded
                    // fallback RVA — if it resolves to a valid table pointer the runtime is
                    // ready.  Cap at 60 s; Il2CppDumper has its own 30-retry loop as backup.
                    WaitForTypeInfoTable();

                    eft_dma_radar.Silk.Tarkov.Unity.IL2CPP.Il2CppDumper.Dump();
                    // NOTE: CameraManager.Initialize() (AllCameras sig-scan + camera_offsets.json)
                    // is intentionally deferred to Phase 4 (Aimview). Not needed for Phase 1.

                    SetState(MemoryState.Initializing);
                    Log.WriteLine("[Memory] Game startup OK.");
                    Notify("Game startup OK", NotificationLevel.Info);
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
                try
                {
                    var ct = _cts.Token;

                    using var game = Game = LocalGameWorld.Create(ct);

                    if (game.IsHideout)
                    {
                        if (SilkProgram.Config.HideoutEnabled)
                        {
                            Log.WriteLine("[Memory] Entered hideout.");
                            RunHideoutLoop(game, ct);
                        }
                        else
                        {
                            Log.WriteLine("[Memory] Hideout detected but disabled by config — skipping.");
                        }
                        continue;
                    }

                    Log.WriteLine($"[Memory] Raid started. Map = '{game.MapID}'");
                    OnRaidStarted();
                    game.Start();

                    while (game.InRaid)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (_restartRequested)
                        {
                            _restartRequested = false;
                            RequestRestart();
                            break;
                        }

                        // Check if game process is still alive (expensive, rate-limited)
                        Thread.Sleep(133);
                    }

                    if (!game.InRaid)
                        Log.WriteLine("[Memory] Raid ended (player extracted, died, or left).");
                }
                catch (OperationCanceledException)
                {
                    if (_shutdown) break;
                    Log.WriteLine("[Memory] Radar restart requested.");
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break; // VMM handle disposed — shutdown in progress
                }
                catch (GameNotRunningException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_shutdown) break;
                    Log.WriteLine($"[Memory] CRITICAL in game loop: {ex}");
                    Notify("CRITICAL error in game loop", NotificationLevel.Error);
                    break;
                }
                finally
                {
                    OnRaidStopped();
                    if (!_shutdown)
                        Thread.Sleep(100);
                }
            }

            if (!_shutdown)
            {
                Log.WriteLine("[Memory] Game is no longer running.");
                Notify("Game is no longer running", NotificationLevel.Warning);
            }
        }

        /// <summary>
        /// Lightweight loop for hideout mode. No worker threads, no loot/player tracking.
        /// Automatically refreshes the HideoutManager, then polls until the GameWorld
        /// changes (player queued for raid or returned to main menu).
        /// </summary>
        private static void RunHideoutLoop(LocalGameWorld game, CancellationToken ct)
        {
            OnHideoutEntered();

            try
            {
                // Auto-refresh hideout data on entry (if enabled)
                if (SilkProgram.Config.HideoutAutoRefresh)
                {
                    try
                    {
                        var status = Hideout.RefreshAll();
                        Log.WriteLine($"[Memory] Hideout auto-refresh: {status}");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"[Memory] Hideout auto-refresh failed: {ex.Message}");
                    }
                }
                else
                {
                    Log.WriteLine("[Memory] Hideout auto-refresh disabled by config.");
                }

                // Poll until hideout GameWorld is no longer valid
                int validityTick = 0;
                while (!_shutdown)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_restartRequested)
                    {
                        _restartRequested = false;
                        RequestRestart();
                        break;
                    }

                    // Every 4th tick (~2s) verify the hideout GameWorld is still alive.
                    // When the player queues for a raid or returns to the main menu,
                    // the hideout scene is torn down and MainPlayer becomes invalid.
                    if ((++validityTick & 0x3) == 0 && !game.IsHideoutAlive())
                    {
                        Log.WriteLine("[Memory] Hideout GameWorld no longer valid.");
                        break;
                    }

                    Thread.Sleep(500);
                }

                Log.WriteLine("[Memory] Left hideout.");
            }
            finally
            {
                OnHideoutExited();
            }
        }

        private static volatile bool _restartRequested;

        public static bool RestartRadar
        {
            set { if (InRaid || InHideout) _restartRequested = value; }
        }

        #endregion

        #region Restart

        /// <summary>
        /// Issue a CancellationToken swap to break the current game loop iteration.
        /// </summary>
        public static void RequestRestart()
        {
            // Allow re-detection of the same GameWorld on user-initiated restart
            LocalGameWorld.ClearStaleGuard();
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

        /// <summary>
        /// Spins until EFT's IL2CPP TypeInfoTable pointer is readable, or until the
        /// 60-second timeout expires.  This ensures <see cref="Il2CppDumper"/> does
        /// not fire before the game's own IL2CPP runtime has finished initializing.
        /// </summary>
        private static void WaitForTypeInfoTable()
        {
            var gaBase = GameAssemblyBase;
            if (gaBase == 0) return;

            var rva = Offsets.Special.TypeInfoTableRva;
            if (rva == 0)
            {
                Log.WriteLine("[Memory] TypeInfoTableRva is 0 — skipping pre-dump wait, Il2CppDumper will sig-scan.");
                return;
            }

            const int timeoutMs = 60_000;
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

        /// <summary>Executes a batch scatter read against the game process.</summary>
        public static void ReadScatter(IScatterEntry[] entries, int count, bool useCache = true)
        {
            if (count == 0) return;
            using var scatter = new VmmScatter(VmmOrThrow(), _pid, ToFlags(useCache));

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                if (e.Address == 0 || e.CB == 0 || (uint)e.CB > MAX_READ_SIZE)
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
            => VmmOrThrow().MemReadValue<T>(_pid, addr, ToFlags(useCache));

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
            return VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache))
                ?? throw new VmmException("String read failed.");
        }

        public static string ReadUnityString(ulong addr, int length = 128, bool useCache = true)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, 0, nameof(length));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x1000, nameof(length));
            return VmmOrThrow().MemReadString(_pid, addr + 0x14, length, Encoding.Unicode, ToFlags(useCache))
                ?? throw new VmmException("Unity string read failed.");
        }

        #endregion

        #region Try Read Methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
            => VmmOrThrow().MemReadValue(_pid, addr, out result, ToFlags(useCache));

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
            => VmmOrThrow().MemReadSpan(_pid, addr, buffer, ToFlags(useCache));

        public static bool TryReadString(ulong addr, out string? result, int cb = 128, bool useCache = true)
        {
            result = null;
            if (cb <= 0 || cb > 0x1000) return false;
            result = VmmOrThrow().MemReadString(_pid, addr, cb, Encoding.UTF8, ToFlags(useCache));
            return result is not null;
        }

        public static bool TryReadUnityString(ulong addr, out string? result, int length = 128, bool useCache = true)
        {
            result = null;
            if (length <= 0 || length > 0x1000) return false;
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
            Span<byte> buf = stackalloc byte[sizeof(T)];
            Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buf), value);
            VmmOrThrow().MemWriteSpan(_pid, addr, buf);
        }

        /// <summary>
        /// Write value to memory with verification — retries up to 3 times.
        /// </summary>
        public static unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged
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
            throw new VmmException("Memory write verification failed.");
        }

        /// <summary>Write a buffer of unmanaged values to memory.</summary>
        public static void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            VmmOrThrow().MemWriteSpan(_pid, addr, buffer);
        }

        /// <summary>Write a buffer with verification — retries up to 3 times.</summary>
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
            throw new VmmException("Memory write buffer verification failed.");
        }

        #endregion

        #region Misc

        public static void FullRefresh() => _vmm?.ForceFullRefresh();

        /// <summary>
        /// Throws <see cref="GameNotRunningException"/> if the game process is gone.
        /// </summary>
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

        /// <summary>Creates a new <see cref="VmmScatter"/> against the game process.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter GetScatter(VmmFlags flags) => new(VmmOrThrow(), _pid, flags);

        /// <summary>Creates a new <see cref="VmmScatter"/> against the game process with cache control.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VmmScatter CreateScatter(bool useCache = true) => new(VmmOrThrow(), _pid, ToFlags(useCache));

        /// <summary>Signals the worker to stop, waits for it to exit, and disposes the VMM handle.</summary>
        public static void Close()
        {
            if (_shutdown) return; // idempotent
            _shutdown = true;
            LobbyQuestReader.Stop();
            eft_dma_radar.Silk.Tarkov.QuestPlanner.QuestPlannerWorker.Stop();
            try { _cts.Cancel(); } catch { }

            // Wait for the worker thread to finish — bounded to prevent hung shutdown
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

        /// <summary>Thrown when the game process is no longer running.</summary>
        public sealed class GameNotRunningException : DmaException
        {
            public GameNotRunningException()
                : base("Game process is no longer running.") { }
        }

        #endregion
    }

    /// <summary>Memory module lifecycle state.</summary>
    public enum MemoryState
    {
        NotStarted,
        WaitingForProcess,
        Initializing,
        ProcessFound,
        InRaid,
        InHideout,
    }

    /// <summary>Notification severity used by <see cref="Memory.ShowNotification"/>.</summary>
    public enum NotificationLevel { Info, Warning, Error }
}
