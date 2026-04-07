using System.Collections.Frozen;
using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

using static eft_dma_radar.Silk.Tarkov.Unity.UnitySDK;

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
    internal sealed class RegisteredPlayers : IReadOnlyCollection<Player.Player>
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

        // Maximum transform/rotation init retries before giving up (exponential backoff)
        private const int MaxInitRetries = 10;

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

        public Player.Player? LocalPlayer { get; private set; }
        public ulong LocalPlayerAddr { get; private set; }
        public int Count => _players.Count;

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
            public bool HasError;

            // Transform init retry tracking — exponential backoff for persistent failures
            public int TransformInitFailures;
            public DateTime NextTransformRetry;
            public int RotationInitFailures;
            public DateTime NextRotationRetry;

            public PlayerEntry(ulong playerBase, Player.Player player, bool isObserved)
            {
                Base = playerBase;
                Player = player;
                IsObserved = isObserved;
            }
        }

        /// <summary>
        /// TRS element in a Unity transform hierarchy vertices array.
        /// Layout: t(Vector3) + pad(float) + q(Quaternion) + s(Vector3) + pad(float) = 48 bytes
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct TrsX
        {
            public readonly Vector3 t;    // translation (12 bytes)
            public readonly float _pad0;  // padding (4 bytes)
            public readonly Quaternion q; // rotation (16 bytes)
            public readonly Vector3 s;    // scale (12 bytes)
            public readonly float _pad1;  // padding (4 bytes)
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
        /// Blocks until the local player (MainPlayer) is found.
        /// </summary>
        internal void WaitForLocalPlayer(CancellationToken ct)
        {
            Log.WriteLine("[RegisteredPlayers] Waiting for LocalPlayer...");
            const int maxAttempts = 60;

            for (int i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var mainPlayerPtr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.MainPlayer, false);
                    if (!mainPlayerPtr.IsValidVirtualAddress())
                    {
                        if (i == 0 || i % 10 == 0)
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer ptr invalid: 0x{mainPlayerPtr:X}");
                        ct.WaitHandle.WaitOne(500);
                        continue;
                    }

                    var className = ReadClassName(mainPlayerPtr);
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer=0x{mainPlayerPtr:X} class='{className ?? "<null>"}'");

                    var entry = CreatePlayerEntry(mainPlayerPtr, isLocal: true);
                    if (entry is not null)
                    {
                        LocalPlayer = entry.Player;
                        LocalPlayerAddr = mainPlayerPtr;
                        _players[mainPlayerPtr] = entry;
                        Log.WriteLine($"[RegisteredPlayers] LocalPlayer found: {entry.Player.Name} (class='{className ?? "<null>"}')");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] WaitForLocalPlayer attempt {i}: {ex.Message}");
                }

                ct.WaitHandle.WaitOne(500);
            }

            Log.WriteLine("[RegisteredPlayers] Timeout waiting for LocalPlayer, proceeding anyway.");
        }

        /// <summary>
        /// Registration refresh: reads the player list, discovers new players, removes gone ones.
        /// Called from the slower registration worker thread.
        /// </summary>
        internal void RefreshRegistration()
        {
            ulong rgtPlayersAddr, listItemsPtr;
            int count;

            try
            {
                rgtPlayersAddr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                listItemsPtr = Memory.ReadPtr(rgtPlayersAddr + UnityList.ArrOffset, false);
                count = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_list", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Failed to read player list: {ex.Message}");
                return;
            }

            if (count < 1 || count > MaxPlayerCount)
            {
                _invalidCountStreak++;
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_count", TimeSpan.FromSeconds(10),
                    $"[RegisteredPlayers] Invalid player count: {count} (addr=0x{rgtPlayersAddr:X}), streak={_invalidCountStreak}");

                // Exponential backoff: sleep longer when we keep getting invalid counts (e.g., after raid ends)
                if (_invalidCountStreak > 3)
                {
                    int backoffMs = Math.Min(1000 * _invalidCountStreak, 10_000);
                    Thread.Sleep(backoffMs);
                }
                return;
            }

            _invalidCountStreak = 0;

            ulong[] ptrs;
            try
            {
                ptrs = Memory.ReadArray<ulong>(listItemsPtr + UnityList.ArrStartOffset, count, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_ptrs", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Failed to read pointer array (count={count}): {ex.Message}");
                return;
            }

            // Reuse the HashSet across calls to avoid per-tick allocation
            var seen = _seenSet;
            seen.Clear();
            seen.EnsureCapacity(count);

            int invalidPtrs = 0;
            int newDiscovered = 0;
            int newFailed = 0;

            // Discover new players
            foreach (var ptr in ptrs)
            {
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

            // Update existing players — mark active/inactive based on registration
            UpdateExistingPlayers(seen);
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
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (!entry.Player.IsActive)
                    continue;

                _activeEntries.Add(entry);
                PrepareScatterReads(scatter, entry);
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

                    // Re-init transform if invalidated (with exponential backoff)
                    if (!entry.TransformReady && entry.TransformInitFailures < MaxInitRetries && now >= entry.NextTransformRetry)
                    {
                        TryInitTransform(entry.Base, entry);
                        if (!entry.TransformReady)
                        {
                            entry.TransformInitFailures++;
                            int backoffSec = Math.Min(1 << entry.TransformInitFailures, 30); // 2s, 4s, 8s, … 30s
                            entry.NextTransformRetry = now.AddSeconds(backoffSec);
                            Log.WriteRateLimited(AppLogLevel.Warning,
                                $"reinit_tfm_{kvp.Key:X}", TimeSpan.FromSeconds(5),
                                $"[RegisteredPlayers] Transform init failed for '{entry.Player.Name}' (attempt {entry.TransformInitFailures}/{MaxInitRetries}, next retry in {backoffSec}s)");
                        }
                        else
                        {
                            if (entry.TransformInitFailures > 0)
                                Log.WriteLine($"[RegisteredPlayers] Transform re-init succeeded for '{entry.Player.Name}' after {entry.TransformInitFailures} failures");
                            entry.TransformInitFailures = 0;
                        }
                    }

                    // Re-init rotation if invalidated (with exponential backoff)
                    if (!entry.RotationReady && entry.RotationInitFailures < MaxInitRetries && now >= entry.NextRotationRetry)
                    {
                        TryInitRotation(entry.Base, entry);
                        if (!entry.RotationReady)
                        {
                            entry.RotationInitFailures++;
                            int backoffSec = Math.Min(1 << entry.RotationInitFailures, 30);
                            entry.NextRotationRetry = now.AddSeconds(backoffSec);
                            Log.WriteRateLimited(AppLogLevel.Warning,
                                $"reinit_rot_{kvp.Key:X}", TimeSpan.FromSeconds(5),
                                $"[RegisteredPlayers] Rotation init failed for '{entry.Player.Name}' (attempt {entry.RotationInitFailures}/{MaxInitRetries}, next retry in {backoffSec}s)");
                        }
                        else
                        {
                            if (entry.RotationInitFailures > 0)
                                Log.WriteLine($"[RegisteredPlayers] Rotation re-init succeeded for '{entry.Player.Name}' after {entry.RotationInitFailures} failures");
                            entry.RotationInitFailures = 0;
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
                        Log.WriteLine($"[RegisteredPlayers] Removed '{removed.Player.Name}' ({removed.Player.Type}) @ 0x{key:X} — no longer registered");
                }
            }
        }

        #endregion

        #region Realtime Loop (Scatter)

        /// <summary>
        /// Prepares scatter reads for a single player's position + rotation.
        /// No delegates or callbacks — results are read inline after Execute().
        /// </summary>
        private static void PrepareScatterReads(VmmScatter scatter, PlayerEntry entry)
        {
            if (entry.RotationReady)
                scatter.PrepareReadValue<Vector2>(entry.RotationAddr);

            if (entry.TransformReady)
            {
                int vertexCount = entry.TransformIndex + 1;
                scatter.PrepareReadArray<TrsX>(entry.VerticesAddr, vertexCount);
            }
        }

        /// <summary>
        /// Processes scatter results for a single player after Execute().
        /// Uses consecutive error counting to debounce transient failures.
        /// </summary>
        private static void ProcessScatterResults(VmmScatter scatter, PlayerEntry entry)
        {
            bool rotOk = true;
            bool posOk = true;

            // --- Rotation ---
            if (entry.RotationReady)
            {
                if (scatter.ReadValue<Vector2>(entry.RotationAddr, out var rot))
                {
                    rotOk = SetRotation(entry, rot);
                    if (!rotOk)
                    {
                        Log.WriteRateLimited(AppLogLevel.Warning,
                            $"rot_bad_{entry.Base:X}", TimeSpan.FromSeconds(3),
                            $"[RegisteredPlayers] Bad rotation for '{entry.Player.Name}': X={rot.X:F2} Y={rot.Y:F2} (addr=0x{entry.RotationAddr:X})");
                    }
                }
                else
                {
                    rotOk = false;
                    Log.WriteRateLimited(AppLogLevel.Warning,
                        $"rot_read_{entry.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] Rotation scatter read failed for '{entry.Player.Name}' (addr=0x{entry.RotationAddr:X})");
                }
            }
            else
            {
                Log.WriteRateLimited(AppLogLevel.Debug,
                    $"rot_notready_{entry.Base:X}", TimeSpan.FromSeconds(10),
                    $"[RegisteredPlayers] Rotation not ready for '{entry.Player.Name}' — skipping");
            }

            // --- Position ---
            if (entry.TransformReady)
            {
                int vertexCount = entry.TransformIndex + 1;
                var vertices = scatter.ReadArray<TrsX>(entry.VerticesAddr, vertexCount);
                if (vertices is not null)
                {
                    posOk = ComputeAndSetPosition(entry, vertices);
                    if (!posOk)
                    {
                        Log.WriteRateLimited(AppLogLevel.Warning,
                            $"pos_bad_{entry.Base:X}", TimeSpan.FromSeconds(3),
                            $"[RegisteredPlayers] Position compute failed for '{entry.Player.Name}' (idx={entry.TransformIndex}, verts=0x{entry.VerticesAddr:X})");
                    }
                }
                else
                {
                    posOk = false;
                    Log.WriteRateLimited(AppLogLevel.Warning,
                        $"pos_read_{entry.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] Position scatter read failed for '{entry.Player.Name}' (verts=0x{entry.VerticesAddr:X}, count={vertexCount})");
                }
            }
            else
            {
                posOk = false;
                Log.WriteRateLimited(AppLogLevel.Debug,
                    $"pos_notready_{entry.Base:X}", TimeSpan.FromSeconds(10),
                    $"[RegisteredPlayers] Transform not ready for '{entry.Player.Name}' — skipping");
            }

            // --- Error state with debounce ---
            bool tickFailed = !rotOk || !posOk;
            if (tickFailed)
            {
                entry.ConsecutiveErrors++;
                if (entry.ConsecutiveErrors >= ErrorThreshold && !entry.HasError)
                {
                    entry.HasError = true;
                    entry.Player.IsError = true;
                    Log.WriteLine($"[RegisteredPlayers] Player '{entry.Player.Name}' entered error state after {entry.ConsecutiveErrors} consecutive failures (rot={rotOk}, pos={posOk})");
                }
            }
            else
            {
                if (entry.HasError)
                {
                    Log.WriteLine($"[RegisteredPlayers] Player '{entry.Player.Name}' recovered from error state");
                }
                entry.ConsecutiveErrors = 0;
                entry.HasError = false;
                entry.Player.IsError = false;
            }
        }

        /// <summary>
        /// Validates and applies a rotation reading.
        /// </summary>
        private static bool SetRotation(PlayerEntry entry, Vector2 rotation)
        {
            if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y))
                return false;

            // Normalize accumulated yaw to [0, 360)
            float x = rotation.X % 360f;
            if (x < 0f) x += 360f;

            entry.Player.RotationYaw = x;
            return true;
        }

        /// <summary>
        /// Computes the world position from a pre-read vertices array and applies it.
        /// </summary>
        private static bool ComputeAndSetPosition(PlayerEntry entry, TrsX[] vertices)
        {
            try
            {
                var indices = entry.CachedIndices!;
                var worldPos = vertices[entry.TransformIndex].t;
                int idx = indices[entry.TransformIndex];
                int iterations = 0;

                while (idx >= 0)
                {
                    if (iterations++ > MaxHierarchyIterations)
                        return false;

                    var parent = vertices[idx];
                    worldPos = Vector3.Transform(worldPos, parent.q);
                    worldPos *= parent.s;
                    worldPos += parent.t;

                    idx = indices[idx];
                }

                if (float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z))
                {
                    entry.Player.Position = worldPos;
                    entry.Player.HasValidPosition = true;
                    return true;
                }

                return false;
            }
            catch (IndexOutOfRangeException)
            {
                // Transient: DMA returned garbage vertices but the transform cache is likely still valid.
                // The error counter in ProcessScatterResults will handle repeated failures.
                return false;
            }
            catch
            {
                // Structural failure (e.g., null CachedIndices) — invalidate transform cache.
                entry.TransformReady = false;
                return false;
            }
        }

        #endregion

        #region Transform Validation (Scatter)

        /// <summary>
        /// Validates that cached transform addresses are still correct.
        /// Uses a two-round scatter pattern for validation.
        /// Round 1: read Hierarchy ptr from TransformInternal.
        /// Round 2: read VerticesAddr from Hierarchy — compare with cached value.
        /// </summary>
        internal void ValidateTransforms()
        {
            // Collect active+transform-ready entries without LINQ allocation
            _validateEntries.Clear();
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (entry.Player.IsActive && entry.TransformReady)
                    _validateEntries.Add(entry);
            }

            if (_validateEntries.Count == 0)
                return;

            // Round 1: read Hierarchy ptr for each entry — inline, no delegate closures
            using var round1 = Memory.GetScatter(VmmFlags.NOCACHE);
            foreach (var entry in _validateEntries)
                round1.PrepareReadValue<ulong>(entry.TransformInternal + TransformAccess.HierarchyOffset);
            round1.Execute();

            // Collect hierarchy results and prepare round 2
            using var round2 = Memory.GetScatter(VmmFlags.NOCACHE);
            Span<ulong> hierarchies = _validateEntries.Count <= 256
                ? stackalloc ulong[_validateEntries.Count]
                : new ulong[_validateEntries.Count];

            for (int i = 0; i < _validateEntries.Count; i++)
            {
                var entry = _validateEntries[i];
                if (round1.ReadValue<ulong>(entry.TransformInternal + TransformAccess.HierarchyOffset, out var hierarchy)
                    && hierarchy.IsValidVirtualAddress())
                {
                    hierarchies[i] = hierarchy;
                    round2.PrepareReadValue<ulong>(hierarchy + TransformHierarchy.VerticesOffset);
                }
            }
            round2.Execute();

            // Process round 2 results — compare vertices with cached value
            for (int i = 0; i < _validateEntries.Count; i++)
            {
                var hierarchy = hierarchies[i];
                if (hierarchy == 0)
                    continue;

                var entry = _validateEntries[i];
                if (round2.ReadValue<ulong>(hierarchy + TransformHierarchy.VerticesOffset, out var verticesPtr)
                    && verticesPtr != entry.VerticesAddr)
                {
                    Log.WriteLine($"[RegisteredPlayers] Transform changed for '{entry.Player.Name}' — re-initializing");
                    entry.TransformReady = false;
                    entry.TransformInitFailures = 0;
                    entry.NextTransformRetry = default;
                    TryInitTransform(entry.Base, entry);
                }
            }
        }

        #endregion

        #region Player Discovery

        /// <summary>
        /// Reads name, side, and allocates a <see cref="PlayerEntry"/> for a new player address.
        /// For observed AI players, reads voice line for boss/raider/scav classification.
        /// Returns null if the read fails or data looks invalid.
        /// </summary>
        private PlayerEntry? CreatePlayerEntry(ulong playerBase, bool isLocal)
        {
            try
            {
                var className = ReadClassName(playerBase);
                bool isObserved = !isLocal && className is not (null or "ClientPlayer" or "LocalPlayer");

                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] CreatePlayerEntry 0x{playerBase:X} isLocal={isLocal} class='{className ?? "<null>"}' isObserved={isObserved}");

                string name;
                int sideRaw;
                PlayerType type;

                if (isObserved)
                {
                    sideRaw = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Side, false);
                    bool isScav = sideRaw == 4; // EPlayerSide.Savage

                    if (isScav)
                    {
                        var isAI = Memory.ReadValue<bool>(playerBase + Offsets.ObservedPlayerView.IsAI, false);
                        if (isAI)
                        {
                            // AI scav — identify by voice line
                            var voicePtr = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.Voice, false);
                            var voice = Memory.ReadUnityString(voicePtr, 64, false);
                            var role = GetInitialAIRole(voice);
                            name = role.Name;
                            type = role.Type;
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   AI scav: voice='{voice ?? "<null>"}' → {role.Name} ({role.Type})");
                        }
                        else
                        {
                            // Player scav
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = $"PScav{id}";
                            type = PlayerType.PScav;
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   Player scav: id={id}");
                        }
                    }
                    else
                    {
                        // PMC (USEC/BEAR)
                        var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                        var side = sideRaw == 1 ? "Usec" : "Bear";
                        name = $"{side}{id}";
                        type = sideRaw == 1 ? PlayerType.USEC : PlayerType.BEAR;
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   PMC: side={sideRaw} ({side}), id={id}");
                    }
                }
                else
                {
                    // Local / Client player — read from profile
                    var profilePtr = Memory.ReadPtr(playerBase + Offsets.Player.Profile, false);
                    var infoPtr = Memory.ReadPtr(profilePtr + Offsets.Profile.Info, false);
                    var nicknamePtr = Memory.ReadPtr(infoPtr + Offsets.PlayerInfo.Nickname, false);
                    name = Memory.ReadUnityString(nicknamePtr, 64, false);
                    sideRaw = Memory.ReadValue<int>(infoPtr + Offsets.PlayerInfo.Side, false);
                    type = isLocal ? PlayerType.Default : ResolveClientPlayerType(sideRaw);
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   Client player: name='{name}' side={sideRaw} type={type}");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] Rejected player 0x{playerBase:X}: empty name (class='{className}', observed={isObserved})");
                    return null;
                }

                Player.Player player = isLocal
                    ? new LocalPlayer { Name = name, Type = type, IsAlive = true, IsActive = true }
                    : new Player.Player { Name = name, Type = type, IsAlive = true, IsActive = true };

                var entry = new PlayerEntry(playerBase, player, isObserved);

                // Pre-warm caches so the first draw tick has position/rotation
                TryInitTransform(playerBase, entry);
                TryInitRotation(playerBase, entry);

                // Assign spawn-group based on initial position proximity (for human players)
                if (!isLocal && player.IsHuman && entry.TransformReady)
                    player.SpawnGroupID = GetOrAssignSpawnGroup(player.Position);

                Log.WriteLine($"[RegisteredPlayers] Discovered: {player} @ 0x{playerBase:X} (class='{className}', observed={isObserved}, " +
                    $"transformReady={entry.TransformReady}, rotationReady={entry.RotationReady}, pos={player.Position})");

                return entry;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] CreatePlayerEntry FAILED 0x{playerBase:X} isLocal={isLocal}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Transform / Rotation Init

        private static void TryInitTransform(ulong playerBase, PlayerEntry entry)
        {
            try
            {
                uint bodyOffset = entry.IsObserved
                    ? Offsets.ObservedPlayerView.PlayerBody
                    : Offsets.Player._playerBody;

                // Walk pointer chain: PlayerBody → SkeletonRootJoint → _values → arr → bone[0] → TransformInternal
                var bodyPtr = Memory.ReadPtr(playerBase + bodyOffset, false);
                var skelRootJoint = Memory.ReadPtr(bodyPtr + Offsets.PlayerBody.SkeletonRootJoint, false);
                var dizValues = Memory.ReadPtr(skelRootJoint + Offsets.DizSkinningSkeleton._values, false);
                var arrPtr = Memory.ReadPtr(dizValues + UnityList.ArrOffset, false);
                var boneEntryPtr = Memory.ReadPtr(arrPtr + UnityList.ArrStartOffset, false);
                var transformInternal = Memory.ReadPtr(boneEntryPtr + 0x10, false);

                // TransformAccess fields are embedded directly in TransformInternal
                var taIndex = Memory.ReadValue<int>(transformInternal + TransformAccess.IndexOffset, false);
                var taHierarchy = Memory.ReadPtr(transformInternal + TransformAccess.HierarchyOffset, false);

                if (taIndex < 0 || taIndex > 128_000)
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitTransform '{entry.Player.Name}' 0x{playerBase:X}: bad taIndex={taIndex}");
                    return;
                }
                if (!taHierarchy.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitTransform '{entry.Player.Name}' 0x{playerBase:X}: invalid hierarchy ptr 0x{taHierarchy:X}");
                    return;
                }

                var verticesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.VerticesOffset, false);
                var indicesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.IndicesOffset, false);

                if (!verticesAddr.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitTransform '{entry.Player.Name}' 0x{playerBase:X}: invalid vertices ptr 0x{verticesAddr:X}");
                    return;
                }
                if (!indicesAddr.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitTransform '{entry.Player.Name}' 0x{playerBase:X}: invalid indices ptr 0x{indicesAddr:X}");
                    return;
                }

                // Cache indices once — they never change for the life of the transform
                int count = taIndex + 1;
                var indices = Memory.ReadArray<int>(indicesAddr, count, false);

                entry.TransformInternal = transformInternal;
                entry.TransformIndex = taIndex;
                entry.VerticesAddr = verticesAddr;
                entry.CachedIndices = indices;
                entry.TransformReady = true;

                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitTransform OK '{entry.Player.Name}': " +
                    $"transformInternal=0x{transformInternal:X}, idx={taIndex}, verts=0x{verticesAddr:X}");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitTransform FAILED '{entry.Player.Name}' 0x{playerBase:X}: {ex.Message}");
                entry.TransformReady = false;
            }
        }

        private static void TryInitRotation(ulong playerBase, PlayerEntry entry)
        {
            try
            {
                ulong rotAddr;
                if (entry.IsObserved)
                {
                    var opc = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, false);
                    var mc = Memory.ReadPtrChain(opc, Offsets.ObservedPlayerController.MovementController, false);
                    rotAddr = mc + GameSDK.Rotation.Observed;
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitRotation '{entry.Player.Name}': observed opc=0x{opc:X} mc=0x{mc:X} rotAddr=0x{rotAddr:X}");
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + GameSDK.Rotation.Client;
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitRotation '{entry.Player.Name}': client movCtx=0x{movCtx:X} rotAddr=0x{rotAddr:X}");
                }

                // Validate rotation is sane before caching (only reject non-finite; game yaw accumulates beyond ±360°)
                var rot = Memory.ReadValue<Vector2>(rotAddr, false);
                if (!float.IsFinite(rot.X) || !float.IsFinite(rot.Y))
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitRotation '{entry.Player.Name}': non-finite rotation X={rot.X} Y={rot.Y} (addr=0x{rotAddr:X})");
                    return;
                }

                entry.RotationAddr = rotAddr;
                entry.RotationReady = true;
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitRotation OK '{entry.Player.Name}': initial rot=({rot.X:F1}, {rot.Y:F1})");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] TryInitRotation FAILED '{entry.Player.Name}' 0x{playerBase:X}: {ex.Message}");
                entry.RotationReady = false;
            }
        }

        #endregion

        #region Helpers

        private static string? ReadClassName(ulong playerBase)
        {
            try
            {
                return SilkObjectClass.ReadName(playerBase, 64);
            }
            catch
            {
                return null;
            }
        }

        private static PlayerType ResolveClientPlayerType(int side)
        {
            return side switch
            {
                1 => PlayerType.USEC,
                2 => PlayerType.BEAR,
                4 => PlayerType.AIScav,
                _ => PlayerType.Default
            };
        }

        #endregion

        #region Spawn Group Assignment

        /// <summary>
        /// Tracks a spawn position associated with a group ID.
        /// </summary>
        private sealed class SpawnGroupEntry
        {
            public int GroupId;
            public Vector3 SpawnPosition;
        }

        /// <summary>
        /// Assigns a spawn-group ID based on position proximity.
        /// Players spawning within <see cref="SpawnGroupDistanceSqr"/> of each other
        /// are placed in the same group.
        /// </summary>
        private int GetOrAssignSpawnGroup(Vector3 spawnPos)
        {
            // Check for zero/invalid spawn positions
            if (spawnPos == Vector3.Zero)
                return -1;

            foreach (var group in _spawnGroups)
            {
                if (Vector3.DistanceSquared(group.SpawnPosition, spawnPos) <= SpawnGroupDistanceSqr)
                    return group.GroupId;
            }

            int newId = _nextSpawnGroupId++;
            _spawnGroups.Add(new SpawnGroupEntry { GroupId = newId, SpawnPosition = spawnPos });
            return newId;
        }

        #endregion

        #region AI Role Identification

        /// <summary>
        /// AI role determined by voice line — contains a display name and player type.
        /// </summary>
        private readonly record struct AIRole(string Name, PlayerType Type);

        /// <summary>
        /// Known voice lines mapped to AI roles. Checked first before fallback pattern matching.
        /// </summary>
        private static readonly FrozenDictionary<string, AIRole> _aiRolesByVoice = new Dictionary<string, AIRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["BossSanitar"] = new("Sanitar", PlayerType.AIBoss),
            ["BossBully"] = new("Reshala", PlayerType.AIBoss),
            ["BossGluhar"] = new("Gluhar", PlayerType.AIBoss),
            ["SectantPriest"] = new("Priest", PlayerType.AIBoss),
            ["SectantWarrior"] = new("Cultist", PlayerType.AIRaider),
            ["BossKilla"] = new("Killa", PlayerType.AIBoss),
            ["BossTagilla"] = new("Tagilla", PlayerType.AIBoss),
            ["Boss_Partizan"] = new("Partisan", PlayerType.AIBoss),
            ["BossBigPipe"] = new("Big Pipe", PlayerType.AIBoss),
            ["BossBirdEye"] = new("Birdeye", PlayerType.AIBoss),
            ["BossKnight"] = new("Knight", PlayerType.AIBoss),
            ["Arena_Guard_1"] = new("Arena Guard", PlayerType.AIScav),
            ["Arena_Guard_2"] = new("Arena Guard", PlayerType.AIScav),
            ["Boss_Kaban"] = new("Kaban", PlayerType.AIBoss),
            ["Boss_Kollontay"] = new("Kollontay", PlayerType.AIBoss),
            ["Boss_Sturman"] = new("Shturman", PlayerType.AIBoss),
            ["Zombie_Generic"] = new("Zombie", PlayerType.AIScav),
            ["BossZombieTagilla"] = new("Zombie Tagilla", PlayerType.AIBoss),
            ["Zombie_Fast"] = new("Zombie", PlayerType.AIScav),
            ["Zombie_Medium"] = new("Zombie", PlayerType.AIScav),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Determines the AI role from a voice line string.
        /// Checks the frozen dictionary first, then falls back to pattern matching.
        /// Applies map-based overrides (e.g., laboratory → Raider).
        /// </summary>
        private AIRole GetInitialAIRole(string voiceLine)
        {
            if (string.IsNullOrEmpty(voiceLine))
                return new("Scav", PlayerType.AIScav);

            if (!_aiRolesByVoice.TryGetValue(voiceLine, out var role))
            {
                role = voiceLine switch
                {
                    _ when voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase) => new("Scav", PlayerType.AIScav),
                    _ when voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase) => new("Boss", PlayerType.AIBoss),
                    _ when voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase) => new("Raider", PlayerType.AIRaider),
                    _ when voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase) => new("Raider", PlayerType.AIRaider),
                    _ when voiceLine.Contains("black_division", StringComparison.OrdinalIgnoreCase) => new("BD", PlayerType.AIRaider),
                    _ when voiceLine.Contains("vsrf", StringComparison.OrdinalIgnoreCase) => new("Vsrf", PlayerType.AIRaider),
                    _ when voiceLine.Contains("civilian", StringComparison.OrdinalIgnoreCase) => new("Civ", PlayerType.AIScav),
                    _ => new("Scav", PlayerType.AIScav)
                };
            }

            // Labs override: all non-boss AI → Raider
            if (_mapId == "laboratory" && role.Type != PlayerType.AIBoss)
                role = new("Raider", PlayerType.AIRaider);

            return role;
        }

        #endregion
    }
}
