using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
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

        // Maximum transform/rotation init retries before giving up (exponential backoff)
        private const int MaxInitRetries = 15;

        // Gear refresh interval per player (seconds)
        private const int GearRefreshIntervalSec = 10;

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();
        private HashSet<ulong> _seenSet = new(MaxPlayerCount);

        // Reusable list for active entries — avoids per-tick allocation on the 8ms realtime path
        private readonly List<PlayerEntry> _activeEntries = new(MaxPlayerCount);

        // Reusable list for ValidateTransforms — avoids LINQ/ToArray allocation
        private readonly List<PlayerEntry> _validateEntries = new(MaxPlayerCount);

        // Backoff for repeated invalid player counts (e.g., after raid ends)
        private int _invalidCountStreak;

        // Spawn-group tracking (position-proximity-based)
        private readonly List<SpawnGroupEntry> _spawnGroups = [];
        private int _nextSpawnGroupId = 1;

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
        private sealed class PlayerEntry
        {
            public readonly ulong Base;
            public readonly Player.Player Player;
            public readonly bool IsObserved;

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

            // Transform init retry tracking — exponential backoff for persistent failures
            public int TransformInitFailures;
            public DateTime NextTransformRetry;
            public int RotationInitFailures;
            public DateTime NextRotationRetry;

            // Gear refresh tracking — rate-limited to avoid excessive DMA reads
            public DateTime NextGearRefresh;

            // Look transform (local player only) — used for aimview eye position.
            // Since both main and look chains use _playerLookRaycastTransform, the
            // TransformInternal/Vertices/Indices are identical — no separate fields needed.
            // This flag simply tracks whether the look transform has been synced.
            public volatile bool LookTransformReady;

            public PlayerEntry(ulong playerBase, Player.Player player, bool isObserved)
            {
                Base = playerBase;
                Player = player;
                IsObserved = isObserved;
            }
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
            public Player.Player Current => inner.Current.Value.Player;
            object System.Collections.IEnumerator.Current => Current;
            public bool MoveNext() => inner.MoveNext();
            public void Reset() => inner.Reset();
            public void Dispose() => inner.Dispose();
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
                        LocalPlayerLost = true;

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

                    var entry = CreatePlayerEntry(ptr, isLocal: false);
                    if (entry is not null)
                    {
                        _players.TryAdd(ptr, entry);
                        newDiscovered++;
                    }
                    else
                    {
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
                $"[RealtimeWorker] Active={_activeEntries.Count}, transformReady={transformReady}, rotationReady={rotationReady}, total={_players.Count}");
        }

        #endregion

        #region Player Lifecycle

        /// <summary>
        /// Updates existing player states based on the current registered set.
        /// Uses scatter-batched reads for lifecycle checks.
        /// </summary>
        private void UpdateExistingPlayers(HashSet<ulong> registered)
        {
            List<ulong>? toRemove = null;

            foreach (var kvp in _players)
            {
                var entry = kvp.Value;

                if (registered.Contains(kvp.Key))
                {
                    // Player still registered — mark active
                    entry.Player.IsActive = true;
                    entry.Player.IsAlive = true;

                    var now = DateTime.UtcNow;

                    // Transform + rotation init/re-init is handled by BatchInitTransformsAndRotations()
                    // which runs before UpdateExistingPlayers in the registration worker cycle.

                    // Gear refresh (rate-limited per player)
                    if (now >= entry.NextGearRefresh)
                    {
                        entry.NextGearRefresh = now.AddSeconds(GearRefreshIntervalSec);
                        GearManager.Refresh(entry.Base, entry.Player, entry.IsObserved);
                        entry.Player.GearReady = true;

                        // Re-check DogtagCache for players with a ProfileId but no resolved name yet.
                        // Corpse dogtags may have been seeded since the last gear refresh.
                        if (entry.Player.ProfileId is not null && entry.Player.AccountId is null)
                            DogtagCache.TryApplyIdentity(entry.Player);
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
                        Log.WriteLine($"[RegisteredPlayers] Removed '{removed.Player.Name}' ({removed.Player.Type}) @ 0x{key:X} — no longer registered");

                        if (removed.Player.IsLocalPlayer)
                        {
                            LocalPlayerLost = true;
                            Log.WriteLine("[RegisteredPlayers] Local player lost — raid is likely ending.");
                        }
                    }
                }
            }
        }

        #endregion
    }
}
