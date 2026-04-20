using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;

using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Manages registered players in a raid — reads, caches, and updates player data.
    /// Supports local player, observed players (PMC, PScav, AI), and voice-based AI identification.
    /// <para>
    /// Uses a two-tier refresh model:
    /// <list type="bullet">
    ///   <item>Registration refresh (slower): reads player list, discovers/removes players, updates lifecycle.</item>
    ///   <item>Realtime refresh (fast): scatter-batched position + rotation for all active players — single DMA round-trip.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed partial class RegisteredPlayers : IReadOnlyCollection<Player.Player>
    {
        #region Constants

        // Maximum parent-chain iterations (safety guard)
        private const int MaxHierarchyIterations = 4000;

        // Maximum valid player count from the registered players list
        private const int MaxPlayerCount = 256;

        // Spawn-group proximity threshold (squared distance, meters²)
        private const float SpawnGroupDistanceSqr = 25f; // 5m radius

        // Number of consecutive realtime failures before entering error state
        private const int ErrorThreshold = 3;

        // Number of consecutive successes required to clear error state (hysteresis prevents flip-flop)
        private const int RecoveryThreshold = 2;

        // Number of consecutive position failures (while TransformReady) that trigger automatic
        // transform invalidation — covers cases where the pointer chain is valid but data isn't populated yet.
        private const int ReinitThreshold = 5;

        // Lower threshold for players that have never had a valid position (just spawned) —
        // their game data is likely still initializing, so re-init faster.
        private const int ReinitThresholdNew = 2;

        // Maximum transform/rotation init retries before giving up (exponential backoff)
        private const int MaxInitRetries = 15;

        // Gear refresh interval per player (seconds)
        private const int GearRefreshIntervalSec = 10;

        // Hands refresh interval per player (seconds) — faster than gear since items swap often
        private const int HandsRefreshIntervalSec = 3;

        // Health status refresh interval per player (seconds) — moderate rate, just a single int read
        private const int HealthRefreshIntervalSec = 3;

        // Local player energy/hydration refresh interval (seconds)
        private const int EnergyHydrationRefreshIntervalSec = 3;

        // ETagStatus flag bits used for health classification
        private const int ETagDying = 8192;
        private const int ETagBadlyInjured = 4096;
        private const int ETagInjured = 2048;

        // Maximum gear + hands refreshes per registration tick — prevents thundering-herd spikes
        // when many players are discovered at once or periodic timers align.
        private const int MaxRefreshesPerTick = 2;

        // Random jitter range (seconds) added to gear refresh intervals to prevent timer re-alignment.
        private const double GearRefreshJitterSec = 2.0;

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();
        private readonly HashSet<ulong> _seenSet = new(MaxPlayerCount);

        // Reusable list for active entries — avoids per-tick allocation on the 8ms realtime path
        private readonly List<PlayerEntry> _activeEntries = new(MaxPlayerCount);

        // Reusable list for ValidateTransforms — avoids LINQ/ToArray allocation
        private readonly List<PlayerEntry> _validateEntries = new(MaxPlayerCount);

        // Backoff for repeated invalid player counts (e.g., after raid ends)
        private int _invalidCountStreak;

        // Monotonic counter used to stagger initial gear/hands refresh times for newly
        // discovered players. Each new player gets a slot offset so refreshes spread
        // across multiple registration ticks instead of thundering-herding.
        private int _staggerIndex;

        // Per-thread RNG for jittering refresh intervals (avoids contention).
        [ThreadStatic] private static Random? t_rng;
        private static Random Rng => t_rng ??= new Random();

        // Spawn-group tracking (position-proximity-based)
        private readonly List<SpawnGroupEntry> _spawnGroups = [];
        private int _nextSpawnGroupId = 1;

        // Backoff for failed CreatePlayerEntry calls — prevents hammering uninitialized objects.
        // Key: player address, Value: (failure count, earliest UTC time to retry).
        // Entries are pruned when the address is either successfully created or removed from the list.
        private readonly Dictionary<ulong, (int Failures, DateTime NextRetry)> _failedEntryBackoff = new();

        // Reusable collections for backoff pruning — avoids per-tick allocation
        private readonly HashSet<ulong> _failedBackoffPrune = new(MaxPlayerCount);
        private readonly List<ulong> _failedBackoffRemove = [];

        #endregion

        #region Properties

        /// <summary>The local player instance, or <c>null</c> if not yet discovered.</summary>
        public Player.Player? LocalPlayer { get; private set; }

        /// <summary>Raw memory address of the local player object (used for raid-ended detection).</summary>
        public ulong LocalPlayerAddr { get; private set; }

        /// <summary>Number of currently tracked players.</summary>
        public int Count => _players.Count;

        /// <summary>
        /// Set when the local player disappears from the game's RegisteredPlayers list.
        /// This is an immediate, high-confidence signal that the raid is ending (death, extraction,
        /// or disconnect). The registration worker uses this to skip expensive secondary work and
        /// trigger an early <c>IsRaidActive()</c> check.
        /// </summary>
        public bool LocalPlayerLost { get; private set; }

        #endregion

        #region Inner Types

        /// <summary>
        /// Pairs a <see cref="Player.Player"/> with its cached transform data so we can avoid
        /// re-walking the pointer chain on every tick.
        /// </summary>
        private sealed class PlayerEntry(ulong playerBase, Player.Player player, bool isObserved)
        {
            public readonly ulong Base = playerBase;
            public readonly Player.Player Player = player;
            public readonly bool IsObserved = isObserved;

            // Cached transform state (populated once, re-validated periodically)
            // Written by registration thread, read by realtime thread — volatile ensures cross-core visibility.
            // The volatile write on TransformReady/RotationReady acts as a release fence, guaranteeing
            // that all preceding data writes (TransformInternal, VerticesAddr, etc.) are visible
            // to the realtime thread that reads the volatile flag as an acquire fence.
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public int TransformIndex;
            public volatile bool TransformReady;

            // Indices never change for the life of the transform — cache once
            public int[]? CachedIndices;

            // Cached rotation address
            public ulong RotationAddr;
            public volatile bool RotationReady;

            // Error tracking for realtime loop — debounce transient failures
            public int ConsecutiveErrors;
            public int RecoveryCount;
            public bool HasError;

            // Set after the first successful realtime position read — distinguishes
            // "init-only position" from "confirmed by realtime scatter loop".
            // Used to select a lower auto-reinit threshold for newly spawned players
            // whose game data may still be initializing.
            public bool RealtimeEstablished;

            // Transform init retry tracking — exponential backoff for persistent failures
            public int TransformInitFailures;
            public DateTime NextTransformRetry;
            public int RotationInitFailures;
            public DateTime NextRotationRetry;

            // Gear refresh tracking — rate-limited to avoid excessive DMA reads
            public DateTime NextGearRefresh;

            // Hands refresh tracking — more frequent than gear
            public DateTime NextHandsRefresh;

            // Look transform (local player only) — used for aimview eye position.
            // Since both main and look chains use _playerLookRaycastTransform, the
            // TransformInternal/Vertices/Indices are identical — no separate fields needed.
            // This flag simply tracks whether the look transform has been synced.
            public volatile bool LookTransformReady;

            // Cached PWA _isAiming address (local player only) — set once during discovery,
            // batched into the realtime scatter so the ADS state is read without extra DMA calls.
            public ulong IsAimingAddr;

            // Observed health controller address — resolved once during discovery for observed players.
            // Used by the registration worker to periodically read HealthStatus.
            public ulong ObservedHealthControllerAddr;

            // Health refresh tracking — rate-limited like gear/hands
            public DateTime NextHealthRefresh;

            // Per-player skeleton — created on the registration worker, updated on the camera worker.
            // Written by registration/camera worker, read by render thread — volatile on the skeleton ref
            // ensures cross-core visibility.
            public volatile Player.Skeleton? Skeleton;
        }

        #endregion

        #region Constructor

        internal RegisteredPlayers(ulong gameWorldBase, string mapId)
        {
            _gameWorldBase = gameWorldBase;
            _mapId = mapId;
        }

        #endregion

        #region IReadOnlyCollection

        public IEnumerator<Player.Player> GetEnumerator() =>
            new PlayerEnumerator(_players.GetEnumerator());

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        /// <summary>
        /// Projecting enumerator: wraps ConcurrentDictionary's enumerator and projects
        /// KeyValuePair → Player. Avoids the extra LINQ Select iterator allocation.
        /// </summary>
        private struct PlayerEnumerator(IEnumerator<KeyValuePair<ulong, PlayerEntry>> inner) : IEnumerator<Player.Player>
        {
            public readonly Player.Player Current => inner.Current.Value.Player;
            readonly object System.Collections.IEnumerator.Current => Current;
            public readonly bool MoveNext() => inner.MoveNext();
            public readonly void Reset() => inner.Reset();
            public readonly void Dispose() => inner.Dispose();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Non-blocking single attempt to discover the local player (MainPlayer).
        /// Called by the registration worker on each tick until successful.
        /// Returns <c>true</c> once the local player is registered.
        /// </summary>
        internal bool TryDiscoverLocalPlayer()
        {
            if (LocalPlayer is not null)
                return true;

            var mainPlayerPtr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.MainPlayer, false);
            if (!mainPlayerPtr.IsValidVirtualAddress())
                return false;

            var className = ReadClassName(mainPlayerPtr);
            var entry = CreatePlayerEntry(mainPlayerPtr, isLocal: true);
            if (entry is null)
                return false;

            LocalPlayer = entry.Player;
            LocalPlayerAddr = mainPlayerPtr;
            entry.Player.Base = mainPlayerPtr;
            _players[mainPlayerPtr] = entry;
            Log.WriteLine($"[RegisteredPlayers] LocalPlayer found: {entry.Player.Name} (class='{className ?? "<null>"}')");
            return true;
        }

        /// <summary>
        /// Registration refresh: reads the player list, discovers new players, removes gone ones.
        /// Called from the slower registration worker thread.
        /// </summary>
        internal void RefreshRegistration()
        {
            ulong rgtPlayersAddr;
            MemList<ulong> ptrs;

            try
            {
                rgtPlayersAddr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                ptrs = MemList<ulong>.Get(rgtPlayersAddr, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_list", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Failed to read player list: {ex.Message}");
                return;
            }

            using (ptrs)
            {
                var count = ptrs.Count;
                if (count < 1 || count > MaxPlayerCount)
                {
                    _invalidCountStreak++;
                    Log.WriteRateLimited(AppLogLevel.Warning, "rp_count", TimeSpan.FromSeconds(10),
                        $"[RegisteredPlayers] Invalid player count: {count} (addr=0x{rgtPlayersAddr:X}), streak={_invalidCountStreak}");

                    // Player count dropping to 0 is a strong signal the raid has ended
                    if (count == 0 && LocalPlayer is not null)
                    {
                        LocalPlayerLost = true;
                        LocalPlayer = null;
                    }

                    // Exponential backoff: sleep longer when we keep getting invalid counts (e.g., after raid ends)
                    if (_invalidCountStreak > 3)
                    {
                        int backoffMs = Math.Min(1000 * _invalidCountStreak, 10_000);
                        Thread.Sleep(backoffMs);
                    }
                    return;
                }

                _invalidCountStreak = 0;

                // Reuse the HashSet across calls to avoid per-tick allocation
                var seen = _seenSet;
                seen.Clear();
                seen.EnsureCapacity(count);

                int invalidPtrs = 0;
                int newDiscovered = 0;
                int newFailed = 0;
                var now = DateTime.UtcNow;

                // Prune backoff entries for addresses no longer in the player list
                // (player was removed before we ever managed to create it).
                if (_failedEntryBackoff.Count > 0)
                {
                    // Build a quick set of current list addresses for O(1) lookup
                    _failedBackoffPrune.Clear();
                    for (int i = 0; i < ptrs.Count; i++)
                    {
                        var p = ptrs[i];
                        if (p.IsValidVirtualAddress())
                            _failedBackoffPrune.Add(p);
                    }

                    // Remove backoff entries that are no longer in the player list
                    _failedBackoffRemove.Clear();
                    foreach (var kvp in _failedEntryBackoff)
                    {
                        if (!_failedBackoffPrune.Contains(kvp.Key))
                            _failedBackoffRemove.Add(kvp.Key);
                    }
                    foreach (var key in _failedBackoffRemove)
                        _failedEntryBackoff.Remove(key);
                }

                // Discover new players
                for (int i = 0; i < ptrs.Count; i++)
                {
                    var ptr = ptrs[i];
                    if (!ptr.IsValidVirtualAddress())
                    {
                        invalidPtrs++;
                        continue;
                    }

                    seen.Add(ptr);

                    if (_players.ContainsKey(ptr))
                        continue;

                    // Check backoff — skip addresses that failed recently
                    if (_failedEntryBackoff.TryGetValue(ptr, out var backoff) && now < backoff.NextRetry)
                        continue;

                    var entry = CreatePlayerEntry(ptr, isLocal: false);
                    if (entry is not null)
                    {
                        _players.TryAdd(ptr, entry);
                        _failedEntryBackoff.Remove(ptr);
                        newDiscovered++;
                    }
                    else
                    {
                        // Exponential backoff: 0.5s, 1s, 2s... capped at 5s
                        int failures = _failedEntryBackoff.TryGetValue(ptr, out var prev)
                            ? prev.Failures + 1
                            : 1;
                        double backoffSec = Math.Min(0.5 * Math.Pow(2, failures - 1), 5.0);
                        _failedEntryBackoff[ptr] = (failures, now.AddSeconds(backoffSec));
                        newFailed++;
                    }
                }

                if (newDiscovered > 0 || newFailed > 0 || invalidPtrs > 0)
                {
                    Log.WriteLine($"[RegisteredPlayers] Refresh: list={count}, valid={seen.Count}, invalidPtrs={invalidPtrs}, " +
                        $"new={newDiscovered}, failed={newFailed}, total={_players.Count}");
                }
            }

            // Batch-init transforms and rotations for all entries that need it.
            BatchInitTransformsAndRotations();

            // Update existing players — mark active/inactive based on registration
            UpdateExistingPlayers(_seenSet);
        }

        /// <summary>
        /// Scatter-batched realtime update: reads position + rotation for ALL active players
        /// in a single DMA round-trip. Called from the fast realtime worker thread.
        /// </summary>
        internal void UpdateRealtimeData()
        {
            if (_players.IsEmpty)
                return;

            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            // Collect active entries and prepare scatter reads (no delegates, no allocation)
            _activeEntries.Clear();
            int transformReady = 0, rotationReady = 0;
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (!entry.Player.IsActive)
                    continue;

                _activeEntries.Add(entry);
                PrepareScatterReads(scatter, entry);

                if (entry.TransformReady) transformReady++;
                if (entry.RotationReady) rotationReady++;
            }

            if (_activeEntries.Count == 0)
                return;

            // Execute single DMA round-trip
            scatter.Execute();

            // Process results inline — no delegate allocation
            foreach (var entry in _activeEntries)
            {
                ProcessScatterResults(scatter, entry);
            }

            // Periodic summary (every ~10s)
            Log.WriteRateLimited(AppLogLevel.Info,
                "realtime_summary", TimeSpan.FromSeconds(10),
                $"[RealtimeWorker] Scatter: active={_activeEntries.Count} (position={transformReady}, rotation={rotationReady}), total={_players.Count}");
        }

        #endregion

        #region Player Lifecycle

        /// <summary>
        /// Updates existing player states based on the current registered set.
        /// Budgets expensive gear/hands refreshes to <see cref="MaxRefreshesPerTick"/> per tick
        /// to keep registration worker tick times bounded and predictable.
        /// </summary>
        private void UpdateExistingPlayers(HashSet<ulong> registered)
        {
            List<ulong>? toRemove = null;
            int refreshBudget = MaxRefreshesPerTick;

            foreach (var kvp in _players)
            {
                var entry = kvp.Value;

                if (registered.Contains(kvp.Key))
                {
                    // Player still registered — mark active
                    entry.Player.IsActive = true;
                    entry.Player.IsAlive = true;

                    // Transform + rotation init/re-init is handled by BatchInitTransformsAndRotations()
                    // which runs before UpdateExistingPlayers in the registration worker cycle.

                    // Skip expensive gear/hands work if budget is exhausted this tick.
                    // Players that missed their window will catch up in subsequent ticks.
                    if (refreshBudget <= 0)
                        continue;

                    var now = DateTime.UtcNow;

                    // Gear refresh (rate-limited per player, budgeted per tick)
                    if (now >= entry.NextGearRefresh)
                    {
                        // Jitter the next interval to prevent long-term timer re-alignment
                        double jitter = Rng.NextDouble() * GearRefreshJitterSec;
                        entry.NextGearRefresh = now.AddSeconds(GearRefreshIntervalSec + jitter);
                        GearManager.Refresh(entry.Base, entry.Player, entry.IsObserved);
                        entry.Player.GearReady = true;
                        refreshBudget--;

                        // Boss-guard identification heuristic (map-specific).
                        try { Player.Plugins.GuardManager.Evaluate(entry.Player, Memory.MapID); }
                        catch { /* non-fatal */ }

                        // Stable PMC display name assignment.
                        try { Player.Plugins.PlayerListManager.GetOrAssign(entry.Player); }
                        catch { /* non-fatal */ }

                        // Re-check DogtagCache for players with a ProfileId but no resolved name yet.
                        // Corpse dogtags may have been seeded since the last gear refresh.
                        if (entry.Player.ProfileId is not null && entry.Player.AccountId is null)
                        {
                            if (DogtagCache.TryApplyIdentity(entry.Player) && entry.Player.AccountId is not null)
                                CheckWatchlist(entry.Player);
                        }
                    }

                    // Hands refresh (rate-limited per player, budgeted per tick)
                    if (now >= entry.NextHandsRefresh)
                    {
                        entry.NextHandsRefresh = now.AddSeconds(HandsRefreshIntervalSec);
                        HandsManager.Refresh(entry.Base, entry.Player, entry.IsObserved);
                        entry.Player.HandsReady = true;
                        refreshBudget--;

                        // Firearm detail refresh (fire mode + mag counts) — piggybacks on hands interval.
                        try { Player.Plugins.FirearmManager.Refresh(entry.Base, entry.Player); }
                        catch { /* non-fatal */ }
                    }

                    // Health status refresh — lightweight single int read, not budgeted
                    if (now >= entry.NextHealthRefresh)
                    {
                        entry.NextHealthRefresh = now.AddSeconds(HealthRefreshIntervalSec);

                        if (entry.IsObserved)
                        {
                            // Observed player: read ETagStatus from ObservedHealthController
                            UpdateObservedHealthStatus(entry);
                        }
                        else if (entry.Player is Player.LocalPlayer lp)
                        {
                            // Local player: read energy/hydration
                            lp.UpdateEnergyHydration(entry.Base);
                        }
                    }
                }
                else
                {
                    // Player no longer in the registered list — mark inactive and queue for removal
                    entry.Player.IsActive = false;
                    entry.Player.IsAlive = false;
                    (toRemove ??= []).Add(kvp.Key);
                }
            }

            if (toRemove is not null)
            {
                foreach (var key in toRemove)
                {
                    if (_players.TryRemove(key, out var removed))
                    {
                        HandsManager.ClearCache(key);
                        Log.WriteLine($"[RegisteredPlayers] Removed '{removed.Player.Name}' ({removed.Player.Type}) @ 0x{key:X} — no longer registered");

                        if (removed.Player.IsLocalPlayer)
                        {
                            LocalPlayerLost = true;
                            LocalPlayer = null;
                            Log.WriteLine("[RegisteredPlayers] Local player lost — raid is likely ending.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Reads the ETagStatus bitmask from the ObservedHealthController and maps it
        /// to the simplified <see cref="Player.EHealthStatus"/> enum.
        /// </summary>
        private static void UpdateObservedHealthStatus(PlayerEntry entry)
        {
            var ohc = entry.ObservedHealthControllerAddr;
            if (ohc == 0)
                return; // Not yet resolved — will stay Healthy

            try
            {
                if (!Memory.TryReadValue<int>(ohc + Offsets.ObservedHealthController.HealthStatus, out var tag, false))
                    return;

                // ETagStatus is a [Flags] enum — check from most severe to least
                if ((tag & ETagDying) != 0)
                    entry.Player.HealthStatus = Player.EHealthStatus.Dying;
                else if ((tag & ETagBadlyInjured) != 0)
                    entry.Player.HealthStatus = Player.EHealthStatus.BadlyInjured;
                else if ((tag & ETagInjured) != 0)
                    entry.Player.HealthStatus = Player.EHealthStatus.Injured;
                else
                    entry.Player.HealthStatus = Player.EHealthStatus.Healthy;
            }
            catch
            {
                // Suppressed — transient DMA failure
            }
        }

        #endregion

        #region Skeleton

        // Skeleton init is expensive (~96 sequential DMA reads per player) — limit to once per 500ms.
        private DateTime _nextSkeletonInit;
        private static readonly TimeSpan SkeletonInitInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Attempts to initialize skeletons for players that don't have one yet.
        /// Rate-limited to avoid hammering DMA with pointer chain walks every tick.
        /// Called from the camera worker.
        /// </summary>
        internal void TryInitSkeletons()
        {
            var now = DateTime.UtcNow;
            if (now < _nextSkeletonInit)
                return;
            _nextSkeletonInit = now + SkeletonInitInterval;

            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (!entry.Player.IsActive || !entry.Player.IsAlive)
                    continue;

                // Skip the local player — we don't draw skeleton for ourselves
                if (entry.Player.IsLocalPlayer)
                    continue;

                // Already has a skeleton
                if (entry.Skeleton is not null)
                    continue;

                // Need a valid transform first (position must be working)
                if (!entry.TransformReady)
                    continue;

                entry.Skeleton = Player.Skeleton.TryCreate(entry.Base, entry.IsObserved);

                // Sync to Player for O(1) render-thread access
                if (entry.Skeleton is not null)
                    entry.Player.Skeleton = entry.Skeleton;
            }
        }

        /// <summary>
        /// Updates all active player skeleton bone positions via a single batched DMA scatter.
        /// Called from the camera worker thread.
        /// </summary>
        internal void UpdateSkeletons()
        {
            // Collect active skeletons
            int count = 0;
            Player.Skeleton?[] skeletons = _skeletonUpdateBuf;
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (!entry.Player.IsActive || !entry.Player.IsAlive)
                    continue;

                var skeleton = entry.Skeleton;
                if (skeleton is null || !skeleton.TransformsReady)
                    continue;

                if (count < skeletons.Length)
                    skeletons[count++] = skeleton;
            }

            if (count == 0)
                return;

            // Single scatter for ALL bone vertex arrays across ALL players
            Player.Skeleton.UpdateBonePositionsBatched(skeletons.AsSpan(0, count));

            // Clear refs to avoid holding them across ticks
            Array.Clear(skeletons, 0, count);
        }

        // Reusable buffer for skeleton update — avoids per-tick allocation
        private readonly Player.Skeleton?[] _skeletonUpdateBuf = new Player.Skeleton?[MaxPlayerCount];

        /// <summary>
        /// Drops the cached skeleton for a player so the camera worker re-creates it
        /// from scratch on the next <see cref="TryInitSkeletons"/> pass. Must be called
        /// whenever the player's main Transform changes (re-init, mass invalidation,
        /// auto-invalidation after repeated position failures) because the skeleton
        /// caches bone TransformInternal pointers rooted in the old hierarchy.
        /// </summary>
        private static void InvalidateSkeleton(PlayerEntry entry)
        {
            if (entry.Skeleton is null)
                return;
            entry.Skeleton = null;
            entry.Player.Skeleton = null;
        }



        #endregion
    }
}
