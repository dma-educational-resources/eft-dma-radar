using eft_dma_radar.Silk.Misc.Workers;
using eft_dma_radar.Silk.Tarkov.GameWorld.Exits;
using eft_dma_radar.Silk.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
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
        private volatile bool _disposed;
        private WorkerThread? _realtimeWorker;
        private WorkerThread? _registrationWorker;

        // The address of the LocalPlayer at raid start — used to detect extraction/death
        private ulong _localPlayerAddr;

        // Stale GameWorld rejection: after a raid ends, the GameWorld persists in Unity
        // while the player views stats/loading. Reject it until a new one appears.
        private static ulong _lastDisposedBase;

        /// <summary>
        /// Cached GamePlayerOwner Il2CppClass pointer — resolved once from the TypeInfoTable.
        /// </summary>
        private static ulong _cachedGamePlayerOwnerKlass;

        // Cooldown after raid ends — prevents rapid re-detection of stale GameWorld
        private static long _raidCooldownUntilTicks;

        private static readonly TimeSpan TransformValidationInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RaidEndedCheckInterval = TimeSpan.FromSeconds(3);
        private DateTime _lastRaidEndedCheck;   // set in Start() so first check has a grace period
        private DateTime _lastTransformValidation;

        #endregion

        #region Properties

        /// <summary>Map identifier for the current raid (e.g. "factory4_night", "bigmap").</summary>
        public string MapID { get; }

        /// <summary>Whether the raid is still active (becomes <c>false</c> after disposal).</summary>
        public bool InRaid => !_disposed;

        /// <summary>The registered players manager for this raid session.</summary>
        public RegisteredPlayers RegisteredPlayers => _registeredPlayers;

        /// <summary>The local (MainPlayer) player, or <c>null</c> if not yet discovered.</summary>
        public Player.Player? LocalPlayer => _registeredPlayers.LocalPlayer;

        /// <summary>Current snapshot of loose loot items in the raid.</summary>
        public IReadOnlyList<LootItem> Loot => _lootManager.Loot;

        /// <summary>Current snapshot of corpses in the raid.</summary>
        public IReadOnlyList<LootCorpse> Corpses => _lootManager.Corpses;

        /// <summary>Current snapshot of exfiltration points in the raid.</summary>
        public IReadOnlyList<Exfil>? Exfils => _exfilManager?.Exfils;

        /// <summary>Current snapshot of keyed doors in the raid.</summary>
        public IReadOnlyList<Door> Doors => _interactablesManager.Doors;

        /// <summary>
        /// Clears the stale GameWorld guard and cooldown so a user-initiated restart
        /// can re-detect the same (still-live) GameWorld.
        /// </summary>
        public static void ClearStaleGuard()
        {
            Interlocked.Exchange(ref _lastDisposedBase, 0);
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
            _registeredPlayers = new RegisteredPlayers(gameWorldBase, mapId);
            _lootManager = new LootManager(gameWorldBase);
            _interactablesManager = new InteractablesManager(gameWorldBase);

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
            // Initialise timing baselines — give the raid a grace period before
            // firing raid-ended and transform-validation checks.
            var now = DateTime.UtcNow;
            _lastRaidEndedCheck = now;
            _lastTransformValidation = now;

            // Start workers immediately — registration worker discovers the
            // local player in the background, realtime worker starts reading
            // positions as soon as players are registered.
            _registrationWorker?.Start();
            _realtimeWorker?.Start();
        }

        /// <summary>
        /// Tears down the raid session — stops worker threads, marks the GameWorld as stale,
        /// and begins a cooldown to prevent re-detection of the same GameWorld address.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Record stale GameWorld address so Create() rejects it
            Interlocked.Exchange(ref _lastDisposedBase, _base);

            // Start cooldown to prevent rapid re-detection of the stale GameWorld
            BeginCooldown(12);

            _realtimeWorker?.Dispose();
            _registrationWorker?.Dispose();
            _realtimeWorker = null;
            _registrationWorker = null;

            DogtagCache.Clear();
        }

        #endregion

        #region Workers

        /// <summary>
        /// Realtime work tick (DynamicSleep: 8ms target, AboveNormal priority).
        /// Scatter-batched position + rotation reads — single DMA round-trip per tick.
        /// </summary>
        private void RealtimeWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                _registeredPlayers.UpdateRealtimeData();
            }
            catch (ObjectDisposedException)
            {
                // Race: Vmm handle disposed during scatter.Execute() — raid is ending, safe to ignore.
            }
            catch (VmmException) when (_disposed)
            {
                // Race: scatter failed because we're shutting down.
            }
        }

        /// <summary>
        /// Registration work tick (100ms, BelowNormal priority).
        /// <para>
        /// <b>Priority order (guaranteed):</b>
        /// <list type="number">
        ///   <item>Local player discovery (blocks everything else until found).</item>
        ///   <item>Player list refresh — always runs every tick.</item>
        ///   <item>Early raid-ended detection — if local player was lost, skip expensive work.</item>
        ///   <item>Secondary work (loot, transform validation, raid-ended) — runs only
        ///         after players are handled. New features added here will never starve
        ///         player discovery or registration.</item>
        /// </list>
        /// </para>
        /// </summary>
        private void RegistrationWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed) return;

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

            var regElapsed = Stopwatch.GetElapsedTime(regStart);

            // ── Priority 2.5: Early raid-ended detection ───────────────────────
            // If the local player disappeared from the registered list or player count dropped to 0,
            // this is a high-confidence signal the raid has ended. Skip all expensive secondary work
            // (transform validation, loot refresh, etc.) and immediately verify via IsRaidActive().
            if (_registeredPlayers.LocalPlayerLost)
            {
                Log.WriteLine("[LocalGameWorld] Local player lost — checking raid status...");
                if (!IsRaidActive())
                {
                    Log.WriteLine("[LocalGameWorld] Raid ended (local player lost).");
                    Memory.ShowNotification?.Invoke("Raid has ended", NotificationLevel.Info);
                    Dispose();
                }
                return; // Skip secondary work either way — data is unreliable
            }

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

        /// <summary>
        /// Secondary work that runs after player registration is complete each tick.
        /// All lower-priority tasks go here — loot, transform validation, raid-ended checks.
        /// Adding new features here guarantees they cannot delay player discovery or registration.
        /// </summary>
        private void DoSecondaryWork()
        {
            // Loot refresh (rate-limited internally to once per 5s)
            _lootManager.Refresh();

            // Exfil status refresh
            _exfilManager?.Refresh();

            // Interactables (doors) — discovery + state refresh (rate-limited internally)
            _interactablesManager.Refresh();

            // Periodic transform validation
            var now = DateTime.UtcNow;
            if ((now - _lastTransformValidation) >= TransformValidationInterval)
            {
                _lastTransformValidation = now;
                _registeredPlayers.ValidateTransforms();
            }

            // Periodic raid-ended check (detects death, extraction, stats screen, map change)
            if ((now - _lastRaidEndedCheck) >= RaidEndedCheckInterval)
            {
                _lastRaidEndedCheck = now;
                if (!IsRaidActive())
                {
                    Log.WriteLine("[LocalGameWorld] Raid is no longer active — disposing.");
                    Memory.ShowNotification?.Invoke("Raid has ended", NotificationLevel.Info);
                    Dispose();
                }
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

                // Reset timing baselines now that the local player is confirmed —
                // gives raid-ended and transform checks a fresh grace period.
                var now = DateTime.UtcNow;
                _lastRaidEndedCheck = now;
                _lastTransformValidation = now;

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
        /// Checks whether the current raid is still active by validating:
        /// <list type="number">
        ///   <item>The game process is still running.</item>
        ///   <item>MainPlayer pointer still matches our LocalPlayer (disappears on extract/death → stats → menu).</item>
        ///   <item>RegisteredPlayers count is > 0 (drops to 0 when GameWorld is torn down).</item>
        ///   <item>Map ID hasn't changed (detects map transitions).</item>
        /// </list>
        /// Retries up to 5 times for transient DMA read failures.
        /// </summary>
        private bool IsRaidActive()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    // 1. MainPlayer pointer still valid and matches our LocalPlayer?
                    if (!Memory.TryReadPtr(_base + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayer, false)
                        || mainPlayer == 0
                        || mainPlayer != _localPlayerAddr)
                    {
                        if (attempt == 0)
                            Log.Write(AppLogLevel.Debug, $"[LocalGameWorld] IsRaidActive: MainPlayer mismatch " +
                                $"(read=0x{mainPlayer:X}, expected=0x{_localPlayerAddr:X})");
                        Thread.Sleep(50);
                        continue;
                    }

                    // 2. Player count > 0?
                    var rgtPlayersAddr = Memory.ReadPtr(_base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                    var count = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
                    if (count <= 0)
                    {
                        if (attempt == 0)
                            Log.Write(AppLogLevel.Debug, $"[LocalGameWorld] IsRaidActive: player count={count}");
                        Thread.Sleep(50);
                        continue;
                    }

                    // 3. Map hasn't changed?
                    var currentMapId = ReadMapID(_base);
                    if (!string.IsNullOrEmpty(currentMapId) &&
                        !string.IsNullOrEmpty(MapID) &&
                        !string.Equals(currentMapId, MapID, StringComparison.Ordinal))
                    {
                        Log.WriteLine($"[LocalGameWorld] Map changed: '{MapID}' → '{currentMapId}'. Ending raid.");
                        return false;
                    }

                    return true; // All checks passed
                }
                catch (Memory.GameNotRunningException)
                {
                    return false; // Game process gone
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                        Log.Write(AppLogLevel.Debug, $"[LocalGameWorld] IsRaidActive attempt {attempt}: {ex.Message}");
                    Thread.Sleep(50);
                }
            }

            // All 5 attempts failed — raid has ended
            return false;
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
    }
}
