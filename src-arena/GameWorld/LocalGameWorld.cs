using eft_dma_radar.Arena.GameWorld.Exits;
using eft_dma_radar.Arena.Misc.Workers;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.IL2CPP;

namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Arena match session. Reads players (position + rotation) and match lifecycle.
    /// <para>
    /// <b>Startup model:</b> Once a valid GameWorld is detected, workers start immediately.
    /// The registration worker discovers the local player on its first tick(s) — no blocking.
    /// </para>
    /// <para>
    /// <b>Worker thread model:</b>
    /// <list type="bullet">
    ///   <item><b>RealtimeWorker</b> (8ms target, DynamicSleep, AboveNormal priority) — scatter-batched
    ///   position + rotation for all active players.</item>
    ///   <item><b>RegistrationWorker</b> (100ms target, BelowNormal priority) — player list refresh,
    ///   local player discovery, secondary systems.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class LocalGameWorld : IDisposable
    {
        #region Fields

        private readonly ulong _base;
        private readonly CancellationToken _ct;
        private int _disposed;
        private WorkerThread? _realtimeWorker;
        private WorkerThread? _registrationWorker;
        private WorkerThread? _cameraWorker;

        private readonly RegisteredPlayers _registeredPlayers;
        private CameraManager? _cameraManager;

        // CameraManager deferred-init backoff (match may still be loading on first ticks)
        private DateTime _nextCameraRetry;
        private DateTime _cameraRetryDeadline;
        private int _cameraRetryAttempts;
        private bool _cameraRetryExhaustedLogged;

        private static readonly TimeSpan CameraRetryBudget = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan CameraRetryIntervalFast = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan CameraRetryIntervalSlow = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan CameraRetryIntervalMax  = TimeSpan.FromSeconds(5);
        private const int CameraRetryFastAttempts = 5;
        private const int CameraRetrySlowAttempts = 15;

        private static ulong _lastDisposedBase;
        private static long _matchCooldownUntilTicks;

        #endregion

        #region Properties

        /// <summary>Map identifier for the current match (e.g. "factory4_day", "Sandbox").</summary>
        public string MapID { get; }

        /// <summary>Map display name (friendly, e.g. "Factory").</summary>
        public string MapName => MapNames.GetDisplayName(MapID);

        /// <summary>Whether the match is still active (becomes <c>false</c> after disposal).</summary>
        public bool InMatch => _disposed == 0;

        /// <summary>All currently tracked players (snapshot, safe to enumerate on any thread).</summary>
        public IEnumerable<Player> Players => _registeredPlayers.All;

        /// <summary>The local player, or null if not yet discovered.</summary>
        public Player? LocalPlayer => _registeredPlayers.LocalPlayer;

        #endregion

        #region Factory

        /// <summary>
        /// Resolves a live LocalGameWorld via GOM scan, validates it is a real in-progress match,
        /// then returns a fully-initialised instance. Blocks until found or cancellation.
        /// </summary>
        public static LocalGameWorld Create(CancellationToken ct)
        {
            WaitForCooldown(ct);
            // Cooldown has elapsed — clear the stale-base guard so we can reattach
            // to the same GameWorld instance that the next Arena match reuses.
            Interlocked.Exchange(ref _lastDisposedBase, 0);

            var searchSw = Stopwatch.StartNew();
            bool firstScan = true;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Heartbeat every 5s so the user knows we're alive
                Log.WriteRateLimited(AppLogLevel.Info, "gw_searching", TimeSpan.FromSeconds(5),
                    $"[LocalGameWorld] Scanning for match... (elapsed {(int)searchSw.Elapsed.TotalSeconds}s)");

                try
                {
                    var gameWorld = FindGameWorld(verbose: firstScan);

                    if (gameWorld == 0)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    if (gameWorld == Interlocked.Read(ref _lastDisposedBase))
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "gw_stale", TimeSpan.FromSeconds(10),
                            $"[LocalGameWorld] Stale GameWorld @ 0x{gameWorld:X} — waiting for new match...");
                        Thread.Sleep(1000);
                        continue;
                    }

                    firstScan = false;

                    var mainPlayerOffset = SDK.Offsets.ClientLocalGameWorld.MainPlayer;
                    var registeredPlayersOffset = SDK.Offsets.ClientLocalGameWorld.RegisteredPlayers;
                    var locationIdOffset = SDK.Offsets.ClientLocalGameWorld.LocationId;

                    // Validate MainPlayer is non-zero (indicates active match)
                    if (!Memory.TryReadPtr(gameWorld + mainPlayerOffset, out var mainPlayer, false) || mainPlayer == 0)
                    {
                        Log.WriteRateLimited(AppLogLevel.Info, "gw_no_main_player", TimeSpan.FromSeconds(5),
                            $"[LocalGameWorld] @ 0x{gameWorld:X} — MainPlayer not ready yet");
                        Thread.Sleep(500);
                        continue;
                    }

                    // Validate RegisteredPlayers list has at least 1 entry
                    if (!Memory.TryReadPtr(gameWorld + registeredPlayersOffset, out var regPlayersList, false)
                        || !regPlayersList.IsValidVirtualAddress()
                        || !Memory.TryReadValue<int>(regPlayersList + eft_dma_radar.Arena.Unity.Collections.MemList<ulong>.CountOffset, out var regCount, false)
                        || regCount < 1)
                    {
                        Log.WriteRateLimited(AppLogLevel.Info, "gw_no_players", TimeSpan.FromSeconds(5),
                            $"[LocalGameWorld] @ 0x{gameWorld:X} — RegisteredPlayers not ready yet");
                        Thread.Sleep(500);
                        continue;
                    }

                    // Try to read MapID from LocationId; default to "Sandbox" if unreadable
                    string mapId = "Sandbox";
                    if (Memory.TryReadPtr(gameWorld + locationIdOffset, out var locationIdPtr, false)
                        && locationIdPtr != 0)
                    {
                        string? readMapId = Memory.ReadUnityString(locationIdPtr, 64, false);
                        if (!string.IsNullOrEmpty(readMapId) && readMapId != "unknown")
                            mapId = readMapId;
                    }

                    //if (!MapNames.Names.ContainsKey(mapId))
                    //{
                    //    Log.WriteRateLimited(AppLogLevel.Info, "gw_unknown_map", TimeSpan.FromSeconds(10),
                    //        $"[LocalGameWorld] Map '{mapId}' not in known Arena maps");
                    //    Thread.Sleep(1000);
                    //    continue;
                    //}

                    Interlocked.Exchange(ref _lastDisposedBase, 0);
                    Log.WriteLine($"[LocalGameWorld] Found live GameWorld @ 0x{gameWorld:X}, map = '{mapId}' ({MapNames.GetDisplayName(mapId)}), players = {regCount}");
                    if (Log.EnableDebugLogging)
                        Il2CppDumper.DumpClassFields(gameWorld, "ClientLocalGameWorld (match start)");
                    return new LocalGameWorld(gameWorld, mapId, ct);
                }
                catch (Memory.GameNotRunningException) { throw; }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Info, "gw_search_err", TimeSpan.FromSeconds(5),
                        $"[LocalGameWorld] Scan error: {ex.GetType().Name}: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        }

        private LocalGameWorld(ulong gameWorldBase, string mapId, CancellationToken ct)
        {
            _base = gameWorldBase;
            MapID = mapId;
            _ct = ct;
            _registeredPlayers = new RegisteredPlayers(gameWorldBase, mapId);

            _realtimeWorker = new WorkerThread
            {
                Name = "Realtime Worker",
                ThreadPriority = ThreadPriority.AboveNormal,
                SleepDuration = TimeSpan.FromMilliseconds(8),
                SleepMode = WorkerSleepMode.DynamicSleep
            };
            _realtimeWorker.PerformWork += RealtimeWorker_PerformWork;

            _registrationWorker = new WorkerThread
            {
                Name = "Registration Worker",
                ThreadPriority = ThreadPriority.BelowNormal,
                SleepDuration = TimeSpan.FromMilliseconds(100),
                SleepMode = WorkerSleepMode.DynamicSleep
            };
            _registrationWorker.PerformWork += RegistrationWorker_PerformWork;

            _cameraWorker = new WorkerThread
            {
                Name = "Camera Worker",
                ThreadPriority = ThreadPriority.Normal,
                SleepDuration = TimeSpan.FromMilliseconds(16),
                SleepMode = WorkerSleepMode.DynamicSleep
            };
            _cameraWorker.PerformWork += CameraWorker_PerformWork;

            // CameraManager static data (sig-scans / offset cache) is pre-warmed in Memory
            // startup before any match is joined — same model as EFT. We just push the current
            // viewport resolution here so WorldToScreen has the correct render size.
            CameraManager.UpdateViewportRes(
                ArenaProgram.Config.GameMonitorWidth,
                ArenaProgram.Config.GameMonitorHeight);
        }

        #endregion

        #region Lifecycle

        public void Start()
        {
            // MatchPositionLogger.Open(); // CSV logging disabled
            _registrationWorker?.Start();
            _realtimeWorker?.Start();
            _cameraWorker?.Start();
            Log.WriteLine($"[LocalGameWorld] Workers started. Map: {MapName} ({MapID})");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // MatchPositionLogger.Close(); // CSV logging disabled
            _realtimeWorker?.Dispose();
            _registrationWorker?.Dispose();
            _cameraWorker?.Dispose();
            _realtimeWorker = null;
            _registrationWorker = null;
            _cameraWorker = null;

            // Reset camera readiness so the next match logs a fresh READY confirmation.
            CameraManager.ResetReadiness();

            Interlocked.Exchange(ref _lastDisposedBase, _base);
            BeginCooldown();
            Log.WriteLine($"[LocalGameWorld] Disposed (map: {MapID}).");
        }

        #endregion

        #region Worker Callbacks

        private void RealtimeWorker_PerformWork(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfMatchEnded();
            _registeredPlayers.UpdateRealtimeData();
        }

        private void RegistrationWorker_PerformWork(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfMatchEnded();

            // Arena-specific: the MainPlayer pointer changes on every death/respawn/next round
            // while the GameWorld is reused, so we must re-check it every tick — not only once.
            _registeredPlayers.TryDiscoverLocalPlayer();

            // Refresh the full registered player list (also batch-inits transforms/rotations
            // for any newly-discovered or previously-invalidated players).
            _registeredPlayers.RefreshRegistration();

            // If local player disappears, the match has likely ended
            if (_registeredPlayers.LocalPlayerLost)
            {
                Log.WriteLine("[LocalGameWorld] LocalPlayerLost — disposing match.");
                Dispose();
            }
        }

        private void CameraWorker_PerformWork(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfMatchEnded();

            // Wait until local player is discovered before bothering with camera.
            if (_registeredPlayers.LocalPlayer is null)
            {
                Log.WriteRateLimited(AppLogLevel.Info, "cam_wait_lp", TimeSpan.FromSeconds(5),
                    "[CameraWorker] Waiting for LocalPlayer before camera init...");
                return;
            }

            try
            {
                if (_cameraManager is null)
                {
                    TryDeferredCameraInit();
                    return;
                }

                _cameraManager.UpdateCamera();

                // Skeleton init + bone scatter ride along with the camera worker so
                // they never contend with the realtime position/rotation scatter.
                if (CameraManager.IsReady)
                {
                    _registeredPlayers.BatchInitSkeletons();
                    _registeredPlayers.BatchUpdateSkeletons();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "cam_error", TimeSpan.FromSeconds(5),
                    $"[CameraWorker] Error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Attempts to create the CameraManager on a rate-limited, adaptive schedule.
        /// Retries with backoff (1s → 3s → 5s) until <see cref="CameraRetryBudget"/> elapses.
        /// </summary>
        private void TryDeferredCameraInit()
        {
            var now = DateTime.UtcNow;

            if (_cameraRetryDeadline == default)
                _cameraRetryDeadline = now + CameraRetryBudget;

            if (now >= _cameraRetryDeadline)
            {
                if (!_cameraRetryExhaustedLogged)
                {
                    _cameraRetryExhaustedLogged = true;
                    Log.WriteLine($"[CameraWorker] CameraManager failed after {_cameraRetryAttempts} attempts over {CameraRetryBudget.TotalSeconds:F0}s — disabled for this match.");
                }
                return;
            }

            if (now < _nextCameraRetry)
                return;

            TimeSpan interval =
                _cameraRetryAttempts < CameraRetryFastAttempts ? CameraRetryIntervalFast :
                _cameraRetryAttempts < CameraRetrySlowAttempts ? CameraRetryIntervalSlow :
                                                                 CameraRetryIntervalMax;

            _nextCameraRetry = now + interval;
            _cameraRetryAttempts++;

            _cameraManager = CameraManager.TryCreate();

            if (_cameraManager is not null)
            {
                Log.WriteLine($"[CameraWorker] CameraManager initialized on attempt #{_cameraRetryAttempts} (FPSCamera=0x{_cameraManager.FPSCamera:X}).");
            }
            else
            {
                var remaining = _cameraRetryDeadline - now;
                Log.WriteRateLimited(AppLogLevel.Info, "cam_retry", TimeSpan.FromSeconds(3),
                    $"[CameraWorker] FPS camera not resolvable yet — attempt #{_cameraRetryAttempts} (next in {interval.TotalSeconds:F0}s, {remaining.TotalSeconds:F0}s budget left).");
            }
        }

        private void ThrowIfMatchEnded()
        {
            if (_disposed != 0)
                throw new OperationCanceledException();
        }

        /// <summary>
        /// On-demand IL2CPP field dump for the entire live match state.
        /// Dumps: GameWorld, CameraManager (FPS + Optic), LocalPlayer hierarchy,
        /// and the full hierarchy of every active observed player.
        /// Safe to call from any thread; all reads are non-cached.
        /// </summary>
        internal void DumpAll()
        {
            Log.WriteLine("[Il2CppDumper] ══ DumpAll triggered ══");
            try
            {
                // 1. GameWorld
                Il2CppDumper.DumpClassFields(_base, $"ClientLocalGameWorld @ 0x{_base:X} (map={MapID})");

                // 2. CameraManager objects
                if (_cameraManager is not null)
                {
                    if (_cameraManager.FPSCamera.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(_cameraManager.FPSCamera, $"FPSCamera @ 0x{_cameraManager.FPSCamera:X}");
                    if (_cameraManager.OpticCamera.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(_cameraManager.OpticCamera, $"OpticCamera @ 0x{_cameraManager.OpticCamera:X}");
                }

                // 3. All active players (local + observed)
                _registeredPlayers.DumpAll();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[Il2CppDumper] DumpAll error: {ex.GetType().Name}: {ex.Message}");
            }
            Log.WriteLine("[Il2CppDumper] ══ DumpAll complete ══");
        }

        #endregion

        #region Game World Discovery

        /// <summary>
        /// Cached GamePlayerOwner Il2CppClass pointer — resolved once from the TypeInfoTable.
        /// </summary>
        private static ulong _cachedGamePlayerOwnerKlass;

        /// <summary>
        /// Finds the ClientLocalGameWorld instance.
        /// IL2CPP path only: GamePlayerOwner.static_fields → _myPlayer → GameWorld.
        /// Returns 0 if not found.
        /// </summary>
        private static ulong FindGameWorld(bool verbose = false)
        {
            return TryGetGameWorldViaIL2CPP(out var gameWorld) ? gameWorld : 0;
        }

        /// <summary>
        /// Resolves the GameWorld instance via:
        /// GamePlayerOwner (IL2CPP class) → static_fields → _myPlayer → GameWorld.
        /// </summary>
        private static bool TryGetGameWorldViaIL2CPP(out ulong gameWorld)
        {
            gameWorld = 0;

            var klassPtr = _cachedGamePlayerOwnerKlass;
            if (!klassPtr.IsValidVirtualAddress())
            {
                klassPtr = ResolveGamePlayerOwnerKlass();
                if (!klassPtr.IsValidVirtualAddress())
                    return false;
                _cachedGamePlayerOwnerKlass = klassPtr;
                Log.WriteLine($"[IL2CPP] GamePlayerOwner class @ 0x{klassPtr:X}");
            }

            if (!Memory.TryReadValue<ulong>(klassPtr + SDK.Offsets.Il2CppClass.StaticFields, out var staticFields)
                || !staticFields.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtr(staticFields + SDK.Offsets.GamePlayerOwner._myPlayer, out var myPlayer)
                || !myPlayer.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtr(myPlayer + SDK.Offsets.Player.GameWorld, out gameWorld)
                || !gameWorld.IsValidVirtualAddress())
                return false;

            return true;
        }

        /// <summary>
        /// Resolves the EFT.GamePlayerOwner Il2CppClass pointer from the TypeInfoTable by name.
        /// Caches the TypeIndex for subsequent calls.
        /// </summary>
        private static ulong ResolveGamePlayerOwnerKlass()
        {
            var gaBase = Memory.GameAssemblyBase;
            if (!gaBase.IsValidVirtualAddress() || SDK.Offsets.Special.TypeInfoTableRva == 0)
                return 0;

            if (!Memory.TryReadPtr(gaBase + SDK.Offsets.Special.TypeInfoTableRva, out var tablePtr, false))
                return 0;

            // Fast path: cached TypeIndex
            var typeIndex = SDK.Offsets.Special.GamePlayerOwner_TypeIndex;
            if (typeIndex != 0
                && Memory.TryReadValue<ulong>(tablePtr + (ulong)typeIndex * 8, out var cached)
                && cached.IsValidVirtualAddress())
                return cached;

            // Scan TypeInfoTable for class named "GamePlayerOwner"
            const int maxEntries = 20_000;
            for (int i = 0; i < maxEntries; i++)
            {
                if (!Memory.TryReadValue<ulong>(tablePtr + (ulong)i * 8, out var ptr) || !ptr.IsValidVirtualAddress())
                    continue;
                if (!Memory.TryReadValue<ulong>(ptr + SDK.Offsets.Il2CppClass.Name, out var namePtr) || !namePtr.IsValidVirtualAddress())
                    continue;
                if (!Memory.TryReadString(namePtr, out var name, 64, useCache: false) || name != "GamePlayerOwner")
                    continue;

                SDK.Offsets.Special.GamePlayerOwner_TypeIndex = (uint)i;
                return ptr;
            }

            return 0;
        }

        private static string ReadMapID(ulong gameWorld)
        {
            try
            {
                if (Memory.TryReadPtr(gameWorld + SDK.Offsets.ClientLocalGameWorld.LocationId, out var locationIdPtr, false)
                    && locationIdPtr != 0)
                {
                    return Memory.ReadUnityString(locationIdPtr, 64, false);
                }
            }
            catch { }
            return "unknown";
        }

        #endregion

        #region Cooldown Helpers

        private static void BeginCooldown(int seconds = 3)
        {
            Interlocked.Exchange(ref _matchCooldownUntilTicks,
                DateTime.UtcNow.AddSeconds(seconds).Ticks);
        }

        public static void ClearStaleGuard()
        {
            Interlocked.Exchange(ref _suppressStaleGuard, 1);
            Interlocked.Exchange(ref _matchCooldownUntilTicks, 0);
        }

        private static int _suppressStaleGuard;

        private static void WaitForCooldown(CancellationToken ct)
        {
            var deadlineTicks = Interlocked.Read(ref _matchCooldownUntilTicks);
            if (deadlineTicks <= 0) return;
            var remaining = new DateTime(deadlineTicks, DateTimeKind.Utc) - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            Log.WriteLine($"[LocalGameWorld] Cooldown — waiting {(int)remaining.TotalMilliseconds}ms...");
            ct.WaitHandle.WaitOne(remaining);
        }

        #endregion

        #region Types

        #endregion
    }
}
