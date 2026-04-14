using eft_dma_radar.Silk.Misc.Workers;
using eft_dma_radar.Silk.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using VmmSharpEx;

using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Minimal raid session. Reads players (position + rotation) and raid lifecycle.
    /// <para>
    /// <b>Startup model:</b> Once a valid GameWorld is detected, workers start immediately.
    /// The registration worker discovers the local player on its first tick(s) — no blocking.
    /// The radar shows "Waiting for Raid Start" until the local player's position is available,
    /// then seamlessly transitions to the live radar view. Loot and other players load in
    /// the background and appear as they become ready.
    /// </para>
    /// <para>
    /// <b>Worker thread model:</b>
    /// <list type="bullet">
    ///   <item><b>RealtimeWorker</b> (8ms target, DynamicSleep, AboveNormal priority) — scatter-batched
    ///   position + rotation for all active players in a single DMA round-trip.
    ///   Actual sleep = max(0, 8ms - workTime). <b>Never</b> touches loot or registration.</item>
    ///   <item><b>RegistrationWorker</b> (100ms target, DynamicSleep, BelowNormal priority) — strict priority ordering:
    ///     <list type="number">
    ///       <item><b>Local player discovery</b> — blocks everything until found.</item>
    ///       <item><b>Player list refresh</b> — always runs every tick.</item>
    ///       <item><b>Secondary work</b> (loot, transforms, raid-ended) — runs only after
    ///         players are handled; cannot starve player work.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class LocalGameWorld : IDisposable
    {
        #region Fields

        private readonly ulong _base;
        private readonly CancellationToken _ct;
        private readonly RegisteredPlayers _registeredPlayers;
        private readonly LootManager _lootManager;
        private readonly InteractablesManager _interactablesManager;
        private ExfilManager? _exfilManager;
        private Quests.QuestManager? _questManager;
        private int _disposed; // 0 = active, 1 = disposed (int for Interlocked.Exchange)
        private WorkerThread? _realtimeWorker;
        private WorkerThread? _registrationWorker;

        // The address of the LocalPlayer at raid start — used to detect extraction/death
        private ulong _localPlayerAddr;

        /// <summary>
        /// Address of the last disposed LocalGameWorld instance.
        /// Used to reject stale GameWorld objects that Unity keeps alive
        /// in the scene graph after a raid ends (post-raid menu).
        /// Accessed via <see cref="Interlocked"/> (ulong cannot be volatile).
        /// </summary>
        private static ulong _lastDisposedBase;

        /// <summary>
        /// When set, <see cref="Dispose"/> will NOT record <see cref="_base"/> into
        /// <see cref="_lastDisposedBase"/>.  This allows a user-initiated restart
        /// to re-detect the same (still-live) GameWorld.
        /// Accessed via <see cref="Interlocked"/>.
        /// </summary>
        private static int _suppressStaleGuard;

        /// <summary>
        /// Cached GamePlayerOwner Il2CppClass pointer — resolved once from the TypeInfoTable.
        /// </summary>
        private static ulong _cachedGamePlayerOwnerKlass;

        // Cooldown after raid ends — prevents rapid re-detection of stale GameWorld
        private static long _raidCooldownUntilTicks;

        private static readonly TimeSpan TransformValidationInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastTransformValidation;

        /// <summary>EFT uses this LocationId when the player is in the hideout scene.</summary>
        internal const string HideoutMapID = "hideout";

        // Map-change detection counter — only check every 64 IsRaidActive() calls (expensive string read)
        private int _mapCheckTick;

        #endregion

        #region Properties

        /// <summary>Map identifier for the current raid (e.g. "factory4_night", "bigmap").</summary>
        public string MapID { get; }

        /// <summary>Whether the raid is still active (becomes <c>false</c> after disposal).</summary>
        public bool InRaid => _disposed == 0;

        /// <summary>True when the GameWorld is the hideout scene (no actual raid).</summary>
        public bool IsHideout { get; }

        /// <summary>The registered players manager for this raid session.</summary>
        public RegisteredPlayers RegisteredPlayers => _registeredPlayers;

        /// <summary>The local (MainPlayer) player, or <c>null</c> if not yet discovered.</summary>
        public Player.Player? LocalPlayer => _registeredPlayers.LocalPlayer;

        /// <summary>Current snapshot of loose loot items in the raid.</summary>
        public IReadOnlyList<LootItem> Loot => _lootManager.Loot;

        /// <summary>Current snapshot of corpses in the raid.</summary>
        public IReadOnlyList<LootCorpse> Corpses => _lootManager.Corpses;

        /// <summary>Current snapshot of static containers in the raid.</summary>
        public IReadOnlyList<LootContainer> Containers => _lootManager.Containers;

        /// <summary>Current snapshot of exfiltration points in the raid.</summary>
        public IReadOnlyList<Exfil>? Exfils => _exfilManager?.Exfils;

        /// <summary>Current snapshot of transit points in the raid.</summary>
        public IReadOnlyList<TransitPoint>? Transits => _exfilManager?.Transits;

        /// <summary>Current snapshot of keyed doors in the raid.</summary>
        public IReadOnlyList<Door> Doors => _interactablesManager.Doors;

        /// <summary>Quest manager for the current raid session (null until local player discovered).</summary>
        public Quests.QuestManager? QuestManager => _questManager;

        /// <summary>Quest zone locations for the current map.</summary>
        public IReadOnlyList<Quests.QuestLocation>? QuestLocations => _questManager?.LocationConditions;

        /// <summary>
        /// Suppresses the stale GameWorld guard for the next <see cref="Dispose"/> call
        /// and clears the cooldown, so a user-initiated restart can re-detect the same
        /// (still-live) GameWorld without waiting.
        /// </summary>
        public static void ClearStaleGuard()
        {
            Interlocked.Exchange(ref _suppressStaleGuard, 1);
            Interlocked.Exchange(ref _raidCooldownUntilTicks, 0);
        }

        /// <summary>
        /// Begins a post-raid cooldown period. <see cref="Create"/> will block until
        /// the cooldown expires, preventing rapid re-detection of the stale GameWorld.
        /// </summary>
        private static void BeginCooldown(int seconds = 12)
        {
            Interlocked.Exchange(ref _raidCooldownUntilTicks,
                DateTime.UtcNow.AddSeconds(seconds).Ticks);
        }

        /// <summary>
        /// Blocks until the post-raid cooldown expires (if active).
        /// </summary>
        private static void WaitForCooldown(CancellationToken ct)
        {
            var deadlineTicks = Interlocked.Read(ref _raidCooldownUntilTicks);
            if (deadlineTicks <= 0) return;

            var remaining = new DateTime(deadlineTicks, DateTimeKind.Utc) - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return;

            Log.WriteLine($"[LocalGameWorld] Cooldown active — waiting {(int)remaining.TotalMilliseconds}ms before next raid detection...");
            ct.WaitHandle.WaitOne(remaining);
        }

        #endregion

        #region Factory

        /// <summary>
        /// Resolves a live LocalGameWorld via IL2CPP direct path (primary) or GOM scan (fallback),
        /// validates it is a real in-progress raid (not a stale post-raid GameWorld),
        /// then returns a fully-initialised instance.
        /// Blocks until found or throws if the game process is gone.
        /// </summary>
        public static LocalGameWorld Create(CancellationToken ct)
        {
            // Wait for post-raid cooldown before scanning for a new GameWorld
            WaitForCooldown(ct);

            var processCheckSw = Stopwatch.StartNew();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Rate-limit the expensive FullRefresh+PID check to once per 5s
                if (processCheckSw.ElapsedMilliseconds >= 5000)
                {
                    processCheckSw.Restart();
                    Memory.ThrowIfNotInGame();
                }

                // Resolve MatchingProgressView once, then update the live stage on every tick.
                if (!MatchingProgressResolver.TryGetCached(out _))
                    MatchingProgressResolver.ResolveAsync();

                MatchingProgressResolver.TryUpdateStage();

                try
                {
                    var gameWorld = FindGameWorld();
                    if (gameWorld == 0)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    // Reject stale GameWorld from a previous raid (Unity keeps it alive on stats/loading screen)
                    if (gameWorld == Interlocked.Read(ref _lastDisposedBase))
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "gw_stale", TimeSpan.FromSeconds(10),
                            $"[LocalGameWorld] Stale GameWorld @ 0x{gameWorld:X} — waiting for new raid...");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Validate we are actually in a raid: MainPlayer must be a valid pointer
                    if (!Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayerPtr, false)
                        || mainPlayerPtr == 0)
                    {
                        Log.WriteRateLimited(AppLogLevel.Info, "gw_search", TimeSpan.FromSeconds(5),
                            "[LocalGameWorld] GameWorld found but no MainPlayer yet — waiting for raid...");
                        Thread.Sleep(500);
                        continue;
                    }

                    // ── Phase 1: Structural validation ──────────────────────────
                    // A stale post-raid GameWorld still has valid MainPlayer pointer
                    // and LocationId, but RegisteredPlayers and transforms are dead.
                    // Validate BEFORE constructing the instance (which spawns workers).
                    if (!IsLocalPlayerInRaid(gameWorld))
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "gw_noraid", TimeSpan.FromSeconds(5),
                            "[LocalGameWorld] GameWorld found but player data not ready — waiting...");
                        Thread.Sleep(500);
                        continue;
                    }

                    if (!ValidateTransformReadable(mainPlayerPtr))
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "gw_stale_xform", TimeSpan.FromSeconds(5),
                            $"[LocalGameWorld] GameWorld @ 0x{gameWorld:X} — transform unreadable (stale). Waiting...");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // ── Phase 2: Accept — construct instance ────────────────────
                    // Accepted — clear the stale guard so we don't reject this address
                    // if the user later restarts manually.
                    Interlocked.Exchange(ref _lastDisposedBase, 0);

                    // Matching phase is over — stop the stage poller and freeze the timer
                    MatchingProgressResolver.NotifyRaidStarted();

                    var mapId = ReadMapID(gameWorld);
                    Log.WriteLine($"[LocalGameWorld] Found live GameWorld @ 0x{gameWorld:X}, map = '{mapId}'");
                    return new LocalGameWorld(gameWorld, mapId, ct);
                }
                catch (Memory.GameNotRunningException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Info, "gw_search", TimeSpan.FromSeconds(5),
                        $"[LocalGameWorld] Waiting for raid... ({ex.Message})");
                    Thread.Sleep(500);
                }
            }
        }

        private LocalGameWorld(ulong gameWorldBase, string mapId, CancellationToken ct)
        {
            _base = gameWorldBase;
            MapID = mapId;
            _ct = ct;
            IsHideout = mapId.Equals(HideoutMapID, StringComparison.OrdinalIgnoreCase);

            _registeredPlayers = new RegisteredPlayers(gameWorldBase, mapId);
            _lootManager = new LootManager(gameWorldBase);
            _interactablesManager = new InteractablesManager(gameWorldBase);

            // In hideout mode, skip creating the expensive worker threads.
            // The hideout has no enemies, loot, or exfils to track.
            if (IsHideout)
                return;

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
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Starts worker threads immediately. The registration worker will discover
        /// the local player on its first tick — no blocking wait. The radar shows
        /// "Waiting for Raid Start" until the local player's position is available,
        /// then seamlessly transitions to the live radar view.
        /// </summary>
        public void Start()
        {
            // In hideout mode, no workers to start — the Memory game loop
            // handles the hideout lifecycle directly.
            if (IsHideout)
            {
                Log.WriteLine("[LocalGameWorld] Hideout mode — skipping worker threads.");
                return;
            }

            // Initialise timing baselines — give the raid a grace period before
            // firing transform-validation checks.
            _lastTransformValidation = DateTime.UtcNow;

            // Start workers immediately — registration worker discovers the
            // local player in the background, realtime worker starts reading
            // positions as soon as players are registered.
            _registrationWorker?.Start();
            _realtimeWorker?.Start();
        }

        /// <summary>
        /// Tears down the raid session — stops worker threads, marks the GameWorld as stale,
        /// and begins a cooldown to prevent re-detection of the same GameWorld address.
        /// Thread-safe: only the first caller performs cleanup.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return; // Already disposed by another thread

            // Record stale GameWorld address so Create() rejects it on the post-raid
            // menu screen.  Skip when the user explicitly requested a restart — the
            // GameWorld is still live and should be re-detectable.
            if (Interlocked.Exchange(ref _suppressStaleGuard, 0) == 0)
                Interlocked.Exchange(ref _lastDisposedBase, _base);

            Log.WriteLine("[LocalGameWorld] Disposed — entering cooldown.");

            // Hideout exits need only a brief cooldown — the stale guard + structural
            // validation in Create() already reject dead GameWorlds.  Raids need a longer
            // cooldown because the post-raid stats screen keeps the GameWorld alive.
            BeginCooldown(IsHideout ? 1 : 12);

            _realtimeWorker?.Dispose();
            _registrationWorker?.Dispose();
            _realtimeWorker = null;
            _registrationWorker = null;

            MatchingProgressResolver.Reset();
            DogtagCache.Clear();
        }

        #endregion

        #region Workers

        /// <summary>
        /// Realtime work tick (DynamicSleep: 8ms target, AboveNormal priority).
        /// Scatter-batched position + rotation reads — single DMA round-trip per tick.
        /// <para>
        /// <b>Raid-ended detection:</b> <see cref="ThrowIfRaidEnded"/> runs at the start of every
        /// tick. If the raid has ended (MainPlayer gone, player count 0, etc.), a <see cref="RaidEnded"/>
        /// exception propagates up to the <see cref="WorkerThread"/> loop, which logs it and exits.
        /// This ensures stale-data reads are cut off within one tick (~8ms) of raid end.
        /// </para>
        /// </summary>
        private void RealtimeWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed != 0) return;

            // Fast bail: registration worker already detected local player removal.
            // The stale GameWorld keeps MainPlayer valid for seconds — IsRaidActive()
            // won't catch it, but this flag is set immediately. Without this guard,
            // scatter reads hit freed transform data and cause native AVE (0xFFFFFFFF).
            if (_registeredPlayers.LocalPlayerLost)
            {
                HandleRaidEnded("Realtime Worker");
                return;
            }

            try
            {
                ThrowIfRaidEnded();
                _registeredPlayers.UpdateRealtimeData();
            }
            catch (RaidEnded)
            {
                HandleRaidEnded("Realtime Worker");
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                // Any unhandled exception = game data corrupted → dispose.
                // Mirrors WPF RealtimeWorker catch-all (L803-807).
                Log.WriteRateLimited(AppLogLevel.Warning, "rt_error", TimeSpan.FromSeconds(5),
                    $"[RealtimeWorker] Error: {ex.GetType().Name}: {ex.Message}");
                Dispose();
            }
        }

        /// <summary>
        /// Registration work tick (100ms, BelowNormal priority).
        /// <para>
        /// <b>Priority order (guaranteed):</b>
        /// <list type="number">
        ///   <item>Local player discovery (blocks everything else until found).</item>
        ///   <item>Player list refresh — always runs every tick.</item>
        ///   <item>Secondary work (loot, transform validation) — runs only
        ///         after players are handled. New features added here will never starve
        ///         player discovery or registration.</item>
        /// </list>
        /// </para>
        /// </summary>
        private void RegistrationWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed != 0) return;

            try
            {
                ThrowIfRaidEnded();

                long regStart = Stopwatch.GetTimestamp();

                // ── Priority 1: Local player discovery ─────────────────────────────
                // Until the local player is found, skip ALL other work. The radar shows
                // "Waiting for Raid Start" and transitions seamlessly once position is available.
                if (_localPlayerAddr == 0)
                {
                    if (!TryDiscoverLocalPlayer())
                        return;
                }

                // ── Priority 2: Player registration (always runs every tick) ───────
                _registeredPlayers.RefreshRegistration();

                // Fast bail: if RefreshRegistration just detected the local player was
                // removed, end the raid NOW — don't continue to secondary work.
                if (_registeredPlayers.LocalPlayerLost)
                {
                    Log.WriteLine("[LocalGameWorld] Local player lost — ending raid.");
                    HandleRaidEnded("Registration Worker");
                    return;
                }

                var regElapsed = Stopwatch.GetElapsedTime(regStart);

                // ── Priority 3: Secondary work (never starves player registration) ─
                long secStart = Stopwatch.GetTimestamp();
                DoSecondaryWork();
                var secElapsed = Stopwatch.GetElapsedTime(secStart);

                // Periodic summary (every ~5s)
                Log.WriteRateLimited(AppLogLevel.Info,
                    "reg_worker_timing", TimeSpan.FromSeconds(5),
                    $"[RegistrationWorker] Tick: players={regElapsed.TotalMilliseconds:F1}ms, " +
                    $"world={secElapsed.TotalMilliseconds:F1}ms (loot/exfils/doors/validation), " +
                    $"total={Stopwatch.GetElapsedTime(regStart).TotalMilliseconds:F1}ms, tracked={_registeredPlayers.Count}");
            }
            catch (RaidEnded)
            {
                HandleRaidEnded("Registration Worker");
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "reg_error", TimeSpan.FromSeconds(5),
                    $"[RegistrationWorker] Error: {ex.GetType().Name}: {ex.Message}");
                Dispose();
            }
        }

        /// <summary>
        /// Secondary work that runs after player registration is complete each tick.
        /// All lower-priority tasks go here — loot, transform validation.
        /// Adding new features here guarantees they cannot delay player discovery or registration.
        /// <para>
        /// Raid-ended checks are no longer needed here — <see cref="ThrowIfRaidEnded"/> at the start
        /// of every worker tick handles detection within one tick of the raid ending.
        /// </para>
        /// </summary>
        private void DoSecondaryWork()
        {
            // Loot refresh (rate-limited internally to once per 5s)
            _lootManager.Refresh();

            // Exfil status refresh
            _exfilManager?.Refresh();

            // Quest data refresh (rate-limited internally to once per 2s)
            _questManager?.Refresh();

            // Interactables (doors) — discovery + state refresh (rate-limited internally)
            _interactablesManager.Refresh();

            // Update door-loot proximity flags (cheap — runs on worker, not render thread)
            if (SilkProgram.Config.DoorsOnlyNearLoot)
            {
                var loot = _lootManager.Loot;
                var doors = _interactablesManager.Doors;
                if (loot.Count > 0 && doors.Count > 0)
                {
                    float proxSq = SilkProgram.Config.DoorLootProximity * SilkProgram.Config.DoorLootProximity;
                    for (int i = 0; i < doors.Count; i++)
                        doors[i].UpdateNearLootFlag(loot, proxSq);
                }
            }

            // Periodic transform validation
            var now = DateTime.UtcNow;
            if ((now - _lastTransformValidation) >= TransformValidationInterval)
            {
                _lastTransformValidation = now;
                _registeredPlayers.ValidateTransforms();
            }
        }

        /// <summary>
        /// Attempts to discover and register the local player. Called from the registration
        /// worker on each tick until successful. Once found, captures the player address
        /// for raid-ended detection and refreshes timing baselines.
        /// </summary>
        private bool TryDiscoverLocalPlayer()
        {
            try
            {
                if (!_registeredPlayers.TryDiscoverLocalPlayer())
                    return false;

                _localPlayerAddr = _registeredPlayers.LocalPlayerAddr;

                // Initialize ExfilManager now that we know the local player's side
                var lp = _registeredPlayers.LocalPlayer as Player.LocalPlayer;
                _exfilManager = new ExfilManager(_base, MapID, lp?.IsPmc ?? true);

                // Initialize QuestManager with the local player's profile pointer
                if (lp is not null && lp.ProfilePtr != 0)
                {
                    _questManager = new Quests.QuestManager(lp.ProfilePtr, MapID);
                    Log.WriteLine($"[LocalGameWorld] QuestManager initialized — profile @ 0x{lp.ProfilePtr:X}");
                }

                // Reset timing baselines now that the local player is confirmed —
                // gives transform checks a fresh grace period.
                _lastTransformValidation = DateTime.UtcNow;

                Log.WriteLine($"[LocalGameWorld] Local player discovered — radar is live.");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Debug, "lgw_lp_discover", TimeSpan.FromSeconds(3),
                    $"[LocalGameWorld] Waiting for local player... ({ex.Message})");
                return false;
            }
        }

        #endregion

        #region Raid Active Check

        /// <summary>
        /// Verifies the raid is still active. Called at the start of every worker tick.
        /// If the raid has ended, throws <see cref="RaidEnded"/> which propagates to the
        /// worker's catch block for clean disposal.
        /// <para>
        /// <b>Why 5 attempts:</b> DMA reads can transiently fail due to page-out, cache
        /// eviction, or bus contention. A single false-negative would incorrectly end
        /// a live raid. Five attempts (50ms total) absorbs transient failures while still
        /// detecting a real raid-end within one worker tick.
        /// </para>
        /// </summary>
        /// <exception cref="RaidEnded">Thrown when all 5 attempts confirm the raid is over.</exception>
        private void ThrowIfRaidEnded()
        {
            // Skip until the local player has been discovered — without a stored address,
            // the MainPlayer equality check in IsRaidActive() is meaningless and would
            // immediately false-positive (mainPlayer != 0 when _localPlayerAddr == 0).
            if (_localPlayerAddr == 0)
                return;

            for (int i = 0; i < 5; i++) // Re-attempt if read fails — 5 times
            {
                try
                {
                    if (IsRaidActive())
                        return;
                }
                catch { }
                Thread.Sleep(10); // short delay between attempts
            }

            // Definitively over.
            MatchingProgressResolver.Reset();

            throw new RaidEnded();
        }

        /// <summary>
        /// Checks if the current raid is active, and LocalPlayer is alive/active.
        /// Mirrors WPF IsRaidActive() exactly.
        /// </summary>
        private bool IsRaidActive()
        {
            try
            {
                // 1) MainPlayer sanity
                var mainPlayer = Memory.ReadPtr(_base + Offsets.ClientLocalGameWorld.MainPlayer, false);
                if (!mainPlayer.IsValidVirtualAddress())
                    return false;

                ArgumentOutOfRangeException.ThrowIfNotEqual(mainPlayer, _localPlayerAddr, nameof(mainPlayer));

                // 2) Player count sanity
                var rgtPlayersAddr = Memory.ReadPtr(_base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                var count = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
                if (count <= 0)
                    return false;

                // 3) Map transition detection — but not on every single call
                if ((_mapCheckTick++ & 0x3F) == 0) // every 64 calls
                {
                    var currentMapId = ReadMapID(_base);
                    if (!string.IsNullOrEmpty(currentMapId) &&
                        !string.IsNullOrEmpty(MapID) &&
                        !string.Equals(currentMapId, MapID, StringComparison.Ordinal))
                    {
                        Log.WriteLine($"[LocalGameWorld] Map changed: '{MapID}' → '{currentMapId}'. Ending raid.");
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Handles the <see cref="RaidEnded"/> exception from a worker thread.
        /// Shows a notification and disposes the raid session.
        /// </summary>
        private void HandleRaidEnded(string workerName)
        {
            Log.WriteLine($"[LocalGameWorld] Raid ended (detected by {workerName}).");
            MatchingProgressResolver.Reset();
            NotifyRaidEnded();
            Dispose();
        }

        /// <summary>
        /// Shows a notification that the raid has ended.
        /// </summary>
        private static void NotifyRaidEnded()
        {
            Memory.ShowNotification?.Invoke("Raid has ended", NotificationLevel.Info);
        }

        #endregion

        #region Raid Validation

        /// <summary>
        /// Validates that a GameWorld has a populated RegisteredPlayers list with at least
        /// one valid player entry. A stale post-raid GameWorld often has count == 1 (the
        /// local player) but the entry pointer is garbage, or count drops to 0.
        /// </summary>
        private static bool IsLocalPlayerInRaid(ulong gameWorld)
        {
            try
            {
                // Read MainPlayer
                if (!Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.MainPlayer, out var playerBase, false)
                    || playerBase == 0)
                    return false;

                // Read RegisteredPlayers list
                if (!Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.RegisteredPlayers, out var rgtPlayersAddr, false)
                    || rgtPlayersAddr == 0)
                    return false;

                // Count must be in sane range (List<T>._size at +0x18)
                if (!Memory.TryReadValue<int>(rgtPlayersAddr + 0x18, out var count, false))
                    return false;
                if (count < 1 || count > 100)
                    return false;

                // First player entry must be a valid pointer
                // List<T>._items at +0x10 → array, first element at array + 0x20
                if (!Memory.TryReadPtr(rgtPlayersAddr + List.ArrOffset, out var listBase, false)
                    || listBase == 0)
                    return false;
                if (!Memory.TryReadPtr(listBase + List.ArrStartOffset, out var firstPlayer, false)
                    || firstPlayer == 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attempts to walk the LocalPlayer's transform pointer chain (PlayerBody → skeleton
        /// → bone[0] → TransformInternal → vertices). If any read fails the GameWorld is
        /// stale — Unity hasn't fully torn it down but the underlying data is garbage.
        /// </summary>
        private static bool ValidateTransformReadable(ulong mainPlayerPtr)
        {
            try
            {
                // Walk: MainPlayer → PlayerBody → SkeletonRootJoint → _values → arr → bone[0] → TransformInternal
                if (!Memory.TryReadPtr(mainPlayerPtr + Offsets.Player._playerBody, out var body, false) || body == 0)
                    return false;
                if (!Memory.TryReadPtr(body + Offsets.PlayerBody.SkeletonRootJoint, out var skelRoot, false) || skelRoot == 0)
                    return false;
                if (!Memory.TryReadPtr(skelRoot + Offsets.DizSkinningSkeleton._values, out var dizValues, false) || dizValues == 0)
                    return false;
                if (!Memory.TryReadPtr(dizValues + List.ArrOffset, out var arrPtr, false) || arrPtr == 0)
                    return false;
                if (!Memory.TryReadPtr(arrPtr + List.ArrStartOffset, out var boneEntry, false) || boneEntry == 0)
                    return false;
                if (!Memory.TryReadPtr(boneEntry + 0x10, out var transformInternal, false) || transformInternal == 0)
                    return false;

                // Read TransformAccess index — must be sane
                if (!Memory.TryReadValue<int>(transformInternal + TransformAccess.IndexOffset, out var taIndex, false))
                    return false;
                if (taIndex < 0 || taIndex > 128_000)
                    return false;

                // Read hierarchy pointer — must be valid
                if (!Memory.TryReadPtr(transformInternal + TransformAccess.HierarchyOffset, out var hierarchy, false)
                    || hierarchy == 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Hideout Validation

        /// <summary>
        /// Lightweight check used by the hideout polling loop (no workers running).
        /// Reads MainPlayer from the GameWorld and verifies the pointer is still valid.
        /// Returns <c>false</c> when the player leaves the hideout scene.
        /// </summary>
        internal bool IsHideoutAlive()
        {
            try
            {
                if (!Memory.TryReadPtr(_base + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayer, false))
                    return false;
                if (!mainPlayer.IsValidVirtualAddress())
                    return false;

                // Verify the LocationId still reads as "hideout"
                var currentMap = ReadMapID(_base);
                return currentMap.Equals(HideoutMapID, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Game World Scan

        /// <summary>
        /// Primary: IL2CPP direct path via GamePlayerOwner → _myPlayer → GameWorld.
        /// Fallback: GOM name-based scan.
        /// </summary>
        private static ulong FindGameWorld()
        {
            // Primary: IL2CPP direct path (fastest — ~5 reads)
            if (TryGetGameWorldViaIL2CPP(out var gameWorld))
                return gameWorld;

            // Fallback: GOM name-based scan
            return FindGameWorldViaGOM();
        }

        // ────────────────────────────────────────────────────────────────
        // IL2CPP DIRECT PATH (GamePlayerOwner → _myPlayer → GameWorld)
        // ────────────────────────────────────────────────────────────────

        private static bool TryGetGameWorldViaIL2CPP(out ulong gameWorld)
        {
            gameWorld = 0;

            // Resolve GamePlayerOwner class pointer from TypeInfoTable (once)
            var klassPtr = _cachedGamePlayerOwnerKlass;
            if (!klassPtr.IsValidVirtualAddress())
            {
                klassPtr = ResolveGamePlayerOwnerKlass();
                if (!klassPtr.IsValidVirtualAddress())
                    return false;

                _cachedGamePlayerOwnerKlass = klassPtr;
                Log.WriteLine($"[IL2CPP] GamePlayerOwner class resolved @ 0x{klassPtr:X}");
            }

            // Read static_fields from the Il2CppClass struct
            if (!Memory.TryReadValue<ulong>(
                klassPtr + Offsets.Il2CppClass.StaticFields, out var staticFields))
                return false;

            if (!staticFields.IsValidVirtualAddress())
                return false;

            // Read _myPlayer from static fields
            if (!Memory.TryReadPtr(
                staticFields + Offsets.GamePlayerOwner._myPlayer, out var myPlayer))
                return false;

            // Read GameWorld from the player
            if (!Memory.TryReadPtr(
                myPlayer + Offsets.Player.GameWorld, out gameWorld))
                return false;

            return gameWorld.IsValidVirtualAddress();
        }

        /// <summary>
        /// Resolves the EFT.GamePlayerOwner Il2CppClass pointer from the TypeInfoTable.
        /// Uses the TypeIndex if available, otherwise falls back to scanning by class name.
        /// </summary>
        private static ulong ResolveGamePlayerOwnerKlass()
        {
            var gaBase = Memory.GameAssemblyBase;
            if (!gaBase.IsValidVirtualAddress() || Offsets.Special.TypeInfoTableRva == 0)
                return 0;

            if (!Memory.TryReadPtr(gaBase + Offsets.Special.TypeInfoTableRva, out var tablePtr, false))
                return 0;

            // Fast path: use cached TypeIndex
            var typeIndex = Offsets.Special.GamePlayerOwner_TypeIndex;
            if (typeIndex != 0)
            {
                if (Memory.TryReadValue<ulong>(
                    tablePtr + (ulong)typeIndex * 8, out var ptr) && ptr.IsValidVirtualAddress())
                    return ptr;
            }

            // Slow fallback: scan first N entries for class named "GamePlayerOwner"
            Log.WriteLine("[IL2CPP] GamePlayerOwner TypeIndex not cached, scanning TypeInfoTable...");
            const int maxEntries = 20_000;
            for (int i = 0; i < maxEntries; i++)
            {
                if (!Memory.TryReadValue<ulong>(tablePtr + (ulong)i * 8, out var ptr) || !ptr.IsValidVirtualAddress())
                    continue;

                if (!Memory.TryReadValue<ulong>(ptr + Offsets.Il2CppClass.Name, out var namePtr) || !namePtr.IsValidVirtualAddress())
                    continue;

                if (!Memory.TryReadString(namePtr, out var name, 64, useCache: false) || name is null)
                    continue;

                if (name == "GamePlayerOwner")
                {
                    Log.WriteLine($"[IL2CPP] GamePlayerOwner found at TypeIndex {i}");
                    Offsets.Special.GamePlayerOwner_TypeIndex = (uint)i;
                    return ptr;
                }
            }

            return 0;
        }

        // ────────────────────────────────────────────────────────────────
        // GOM FALLBACK — Name-based scan
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Scans the GOM (Game Object Manager) for a GameObject named "GameWorld"
        /// and walks its component chain to find the ClientLocalGameWorld instance.
        /// </summary>
        private static ulong FindGameWorldViaGOM()
        {
            var gom = Memory.ReadValue<GOM>(Memory.GOM, false);
            var gameObject = gom.GetGameObjectByName("GameWorld");
            if (gameObject == 0) return 0;

            // Walk: GameObject → ComponentArray → entry[1].Component → ObjectClass
            if (!Memory.TryReadPtr(gameObject + GO_Components, out var compArray, false)) return 0;
            if (!Memory.TryReadPtr(compArray + 0x18, out var component, false)) return 0;
            if (!Memory.TryReadPtr(component + Comp_ObjectClass, out var objectClass, false)) return 0;

            return objectClass;
        }

        // ────────────────────────────────────────────────────────────────
        // MAP RESOLUTION
        // ────────────────────────────────────────────────────────────────

        private static string ReadMapID(ulong gameWorld)
        {
            try
            {
                // Primary: LocationId directly from GameWorld
                if (Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.LocationId, out var locationIdPtr, false)
                    && locationIdPtr != 0)
                {
                    return Memory.ReadUnityString(locationIdPtr, 64, false);
                }

                // Fallback: read Location from MainPlayer
                if (Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.MainPlayer, out var lp, false)
                    && lp != 0
                    && Memory.TryReadPtr(lp + Offsets.Player.Location, out var mapPtr, false)
                    && mapPtr != 0)
                {
                    return Memory.ReadUnityString(mapPtr, 64, false);
                }
            }
            catch { }

            return "unknown";
        }

        #endregion

        #region Types

        /// <summary>
        /// Sentinel exception thrown by <see cref="ThrowIfRaidEnded"/> when the raid has definitively ended.
        /// Caught by worker tick handlers to trigger clean disposal.
        /// </summary>
        private sealed class RaidEnded : Exception
        {
            public RaidEnded() { }
        }

        #endregion
    }
}
