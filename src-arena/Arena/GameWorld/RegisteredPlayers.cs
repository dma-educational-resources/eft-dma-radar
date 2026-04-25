using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using eft_dma_radar.Arena.DMA;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.Collections;
using SDK;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

using static eft_dma_radar.Arena.Unity.UnityOffsets;

namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Manages registered players in an Arena match.
    /// <para>
    /// Registration worker (100ms): reads the RegisteredPlayers list, discovers/removes players,
    /// resolves nickname/account/side/type, and initialises transform + rotation caches.
    /// </para>
    /// <para>
    /// Realtime worker (8ms): scatter-batches position + rotation reads for all active players
    /// in a single DMA round-trip.
    /// </para>
    /// </summary>
    internal sealed class RegisteredPlayers
    {
        #region Constants

        private const int MaxPlayerCount       = 64;
        // Arena rounds churn pointers fast — invalidate sooner so retries actually run again.
        private const int ReinitThreshold      = 3;
        private const int ReinitThresholdNew   = 2;

        // Grace period (in registration ticks, ~100ms each) before an observed player who has
        // dropped out of the RegisteredPlayers list is actually removed. Arena's list-read can
        // momentarily return a stale/short snapshot during fast churn / respawns, and many
        // first-chance BadPtr/Vmm exceptions are recoverable. Keeping the entry around for a
        // few extra ticks lets the next tick re-confirm the player instead of churning
        // remove → re-discover (which makes them disappear from the overlay for ~1s+).
        private const int MissingTicksBeforeRemoval = 5; // ~500ms

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;

        private readonly ConcurrentDictionary<ulong, Player> _players = new();
        private readonly HashSet<ulong> _seenSet = new(MaxPlayerCount);
        private readonly List<Player> _activeList = new(MaxPlayerCount);

        // Snapshot of active players, rebuilt by the registration worker after each tick.
        // The realtime worker reads this via Volatile — no per-tick dictionary enumeration.
        private static readonly Player[] _emptyPlayers = [];
        private Player[] _activeSnapshot = _emptyPlayers;

        private int _invalidCountStreak;

        // Team identity is locked at match start. The first time the local player's armband
        // resolves to a valid TeamID we snapshot it here and reuse it for the remainder of the
        // match — across deaths, respawns, and MainPlayer pointer flips. This prevents teammate
        // colors from "swapping" mid-round if a transient armband read on a respawned local
        // entry returns the opposite team. A new match (LocalGameWorld is disposed on
        // LocalPlayerLost) constructs a fresh RegisteredPlayers, so this naturally resets.
        //
        // We do NOT lock on the first successful armband read. At match start Arena populates
        // the equipment slots in stages; an early scan can briefly pick up a stale ContainedItem
        // (leftover from the previous round in the reused GameWorld) or a half-initialised slot
        // pointing at the wrong template. A single bad read at t=0 used to poison the entire
        // round (your teammates got painted as enemies and vice versa). Instead, we require N
        // consecutive registration ticks where the local armband resolves to the SAME team ID
        // before snapshotting it. Until the lock fires, teammate classification falls back to
        // the live per-tick LocalPlayer.TeamID.
        private int _matchLocalTeamId = -1;
        private int _candidateLocalTeamId = -1;
        private int _candidateLocalTeamStreak;
        private const int LocalTeamLockTicks = 3; // ~300ms of agreement before locking

        // Auto-probed offset of <_inventoryController>k__BackingField on Arena's Player class.
        // The hardcoded Offsets.Player._inventoryController is an EFT-mainline guess that
        // resolves to the wrong field in Arena (chain ends up walking GameAssembly metadata).
        // We probe once per RegisteredPlayers instance (i.e. per match) by scanning a range
        // of offsets and picking the one whose chain produces a known armband GUID.
        private bool _localInvCtrlOffsetProbed;
        private uint _localInvCtrlOffsetResolved; // 0 = probe failed, fallback to hardcoded
        private long _localInvCtrlNextProbeTick;  // throttle re-probe attempts when the armband isn't equipped yet

        // Sustained empty/invalid RegisteredPlayers list is the authoritative match-end signal
        // for Arena. The MainPlayer pointer flips to null on every death/respawn within a round,
        // so it cannot be used to detect match end — only an empty RegisteredPlayers list can.
        // When tripped, LocalGameWorld disposes and the next round is reacquired automatically.
        private const int InvalidCountTicksBeforeLost = 30; // ~3s at 100ms tick

        #endregion

        #region Properties

        public Player? LocalPlayer { get; private set; }
        public ulong LocalPlayerAddr { get; private set; }
        public bool LocalPlayerLost { get; private set; }

        /// <summary>Snapshot of all currently tracked players (allocation-free iteration).</summary>
        public IEnumerable<Player> All => _players.Values;
        public int Count => _players.Count;

        #endregion

        internal RegisteredPlayers(ulong gameWorldBase, string mapId)
        {
            _gameWorldBase = gameWorldBase;
            _mapId = mapId;
        }

        // ── Registration worker ───────────────────────────────────────────────

        /// <summary>
        /// Discovers (or re-discovers) the local (MainPlayer) instance from the GameWorld.MainPlayer
        /// pointer. Arena reuses the same GameWorld across deaths/respawns but swaps the MainPlayer
        /// pointer each round, so this is called every registration tick — not just once — and will
        /// replace the cached entry whenever the pointer changes.
        /// Returns true when a valid local player is registered.
        /// </summary>
        internal bool TryDiscoverLocalPlayer()
        {
            if (!Memory.TryReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayerPtr, false)
                || !mainPlayerPtr.IsValidVirtualAddress())
            {
                // MainPlayer temporarily null — this happens on every death/respawn in Arena
                // (the GameWorld is reused across rounds and between deaths). Keep the existing
                // LocalPlayer entry but mark it inactive so the realtime worker stops scattering
                // against a stale ptr. Do NOT trip LocalPlayerLost here — match-end is signalled
                // authoritatively by the RegisteredPlayers list going empty (see RefreshRegistration).
                if (LocalPlayer is not null)
                {
                    LocalPlayer.IsActive = false;
                }
                return LocalPlayer is not null;
            }

            // Same pointer as before → nothing to do, just make sure it's marked active.
            if (LocalPlayer is not null && LocalPlayerAddr == mainPlayerPtr)
            {
                LocalPlayer.IsActive = true;
                LocalPlayer.IsAlive = true;
                return true;
            }

            // Pointer changed (respawn / next round) or first discovery — build a fresh entry.
            var player = CreatePlayerEntry(mainPlayerPtr, isLocal: true);
            if (player is null)
                return LocalPlayer is not null;

            // Drop the old local entry (if any) — its transform/rotation addrs are now stale.
            if (LocalPlayer is not null && LocalPlayerAddr != 0
                && _players.TryRemove(LocalPlayerAddr, out var oldLocal))
            {
                Log.WriteLine($"[RegisteredPlayers] LocalPlayer pointer changed 0x{LocalPlayerAddr:X} -> 0x{mainPlayerPtr:X} (respawn)");
                // Tell the camera worker the FPS camera was almost certainly rebuilt by
                // the respawn — bypass its normal 500ms rate-limit so ESP/Aimview don't
                // render against a stale matrix until the next routine refresh.
                CameraManager.RequestFpsCameraRefresh();
            }

            LocalPlayer = player;
            LocalPlayerAddr = mainPlayerPtr;
            _players[mainPlayerPtr] = player;
            Log.WriteLine($"[RegisteredPlayers] LocalPlayer: {player}");
            return true;
        }

        /// <summary>
        /// Refreshes the registered player list — discovers new, removes gone.
        /// Called from the registration worker thread (~100ms interval).
        /// </summary>
        internal void RefreshRegistration()
        {
            ulong listAddr;
            MemList<ulong> ptrs;

            try
            {
                listAddr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                ptrs = MemList<ulong>.Get(listAddr, false);
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
                var seen = _seenSet;
                if (count < 1 || count > MaxPlayerCount)
                {
                    _invalidCountStreak++;
                    Log.WriteRateLimited(AppLogLevel.Warning, "rp_count", TimeSpan.FromSeconds(10),
                        $"[RegisteredPlayers] Invalid player count: {count} (addr=0x{listAddr:X}), streak={_invalidCountStreak}");

                    // Arena note: the RegisteredPlayers list can legitimately be empty between
                    // rounds / during respawn while the GameWorld is reused. Clear any stale
                    // observed-player entries so the realtime worker stops reading freed memory
                    // (otherwise we get a flood of VmmException), and trip LocalPlayerLost once
                    // the empty streak proves the match has actually ended so the LocalGameWorld
                    // disposes and we re-scan for the next round (no manual restart required).
                    seen.Clear();
                    UpdateExistingPlayers(seen); // observed players cleaned; LocalPlayer kept (skipped by IsLocalPlayer guard)

                    if (_invalidCountStreak >= InvalidCountTicksBeforeLost && !LocalPlayerLost)
                    {
                        Log.WriteLine($"[RegisteredPlayers] RegisteredPlayers list empty for {_invalidCountStreak} ticks — match ended.");
                        LocalPlayerLost = true;
                    }
                    return;
                }

                _invalidCountStreak = 0;

                seen.Clear();

                int newDiscovered = 0, invalidPtrs = 0;

                for (int i = 0; i < ptrs.Count; i++)
                {
                    var ptr = ptrs[i];
                    if (!ptr.IsValidVirtualAddress()) { invalidPtrs++; continue; }
                    seen.Add(ptr);
                    if (_players.ContainsKey(ptr)) continue;

                    var player = CreatePlayerEntry(ptr, isLocal: false);
                    if (player is not null)
                    {
                        _players.TryAdd(ptr, player);
                        newDiscovered++;
                    }
                }

                if (newDiscovered > 0 || invalidPtrs > 0)
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] Refresh: list={count}, new={newDiscovered}, invalid={invalidPtrs}, total={_players.Count}");
            }

            // Update active/inactive state FIRST so IsActive is correct before batch-init
            UpdateExistingPlayers(_seenSet);

            // Batch-init transforms and rotations for all active players without them
            BatchInitTransforms();
            BatchInitRotations();
        }

        private void UpdateExistingPlayers(HashSet<ulong> registered)
        {
            long nowTick = Environment.TickCount64;

            // Once locked, the match-wide team identity is authoritative and is force-applied to
            // the current LocalPlayer entry every tick (across respawns / MainPlayer flips).
            if (LocalPlayer is not null && _matchLocalTeamId >= 0 && LocalPlayer.TeamID != _matchLocalTeamId)
            {
                LocalPlayer.TeamID = _matchLocalTeamId;
            }
            // While not yet locked, keep sampling the local armband each (back-off) tick — even
            // if LocalPlayer.TeamID is already set — and require N consecutive identical reads
            // before snapshotting into _matchLocalTeamId. This prevents a single transient bad
            // read at match start from poisoning teammate classification for the entire round.
            else if (LocalPlayer is not null && _matchLocalTeamId < 0 && nowTick >= LocalPlayer.NextTeamIdTick)
            {
                try
                {
                    // Try the hardcoded Arena-verified offset first (Offsets.Player._inventoryController = 0x9E0).
                    // If a future game patch shifts the field, fall through to the runtime probe which
                    // scans for a chain that produces a known armband GUID and caches the result.
                    uint useOffset = _localInvCtrlOffsetResolved != 0
                        ? _localInvCtrlOffsetResolved
                        : Offsets.Player._inventoryController;

                    bool invOk = Memory.TryReadPtr(LocalPlayer.Base + useOffset, out var invCtrl, false);
                    bool invValid = invOk && invCtrl.IsValidVirtualAddress();
                    int t = invValid ? GetTeamID(LocalPlayer, invCtrl) : -1;

                    // Hardcoded offset failed to produce a known armband — kick off (or retry) the probe.
                    if (t < 0 && !_localInvCtrlOffsetProbed && nowTick >= _localInvCtrlNextProbeTick)
                    {
                        ProbeLocalInventoryControllerOffset(LocalPlayer.Base);
                        if (_localInvCtrlOffsetResolved != 0
                            && Memory.TryReadPtr(LocalPlayer.Base + _localInvCtrlOffsetResolved, out invCtrl, false)
                            && invCtrl.IsValidVirtualAddress())
                        {
                            t = GetTeamID(LocalPlayer, invCtrl);
                        }
                        else
                        {
                            // Probe failed (armband not equipped yet) — back off so we don't
                            // re-scan the entire offset range every team-id tick.
                            _localInvCtrlNextProbeTick = nowTick + 2_000;
                        }
                    }

                    if (!invValid && t < 0)
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "local_team_inv", TimeSpan.FromSeconds(5),
                            $"[RegisteredPlayers] Local inv chain not yet ready: base=0x{LocalPlayer.Base:X} +0x{useOffset:X}");
                        ScheduleTeamIdRetry(LocalPlayer, nowTick);
                    }
                    else
                    {
                        if (t >= 0)
                        {
                            // Always reflect the latest read on the LocalPlayer entry until lock.
                            if (LocalPlayer.TeamID != t)
                                LocalPlayer.TeamID = t;
                            LocalPlayer.TeamIdFailStreak = 0;

                            if (_candidateLocalTeamId == t)
                            {
                                _candidateLocalTeamStreak++;
                            }
                            else
                            {
                                if (_candidateLocalTeamId >= 0)
                                {
                                    Log.WriteLine($"[RegisteredPlayers] Local team candidate changed: {(ArmbandColorType)_candidateLocalTeamId} -> {(ArmbandColorType)t} (resetting stability streak)");
                                }
                                _candidateLocalTeamId = t;
                                _candidateLocalTeamStreak = 1;
                            }

                            if (_candidateLocalTeamStreak >= LocalTeamLockTicks)
                            {
                                _matchLocalTeamId = t;
                                Log.WriteLine($"[RegisteredPlayers] Match team locked: LocalPlayer = {(ArmbandColorType)t} (after {_candidateLocalTeamStreak} consistent reads)");
                            }
                            else
                            {
                                // Sample again on the next tick (no back-off while we're trying
                                // to build the stability streak).
                                LocalPlayer.NextTeamIdTick = nowTick + 100;
                            }
                        }
                        else ScheduleTeamIdRetry(LocalPlayer, nowTick);
                    }
                }
                catch { ScheduleTeamIdRetry(LocalPlayer, nowTick); }
            }

            List<ulong>? toRemove = null;
            foreach (var kvp in _players)
            {
                // Local player is not in the RegisteredPlayers list (it uses MainPlayer ptr),
                // so always keep it active; never mark it for removal here.
                if (kvp.Value.IsLocalPlayer)
                {
                    kvp.Value.IsActive = true;
                    kvp.Value.IsAlive = true;
                    continue;
                }

                if (registered.Contains(kvp.Key))
                {
                    // If a player blinked out of the list (MissingTicks > 0) and is now back at
                    // the same base, Arena may have torn down + rebuilt the transform hierarchy
                    // during the gap. The old VerticesAddr could still be readable but stale,
                    // which would freeze the dot at the last position with no read errors to
                    // trip auto-reinit. Force a transform reinit on re-registration to be safe.
                    if (kvp.Value.MissingTicks > 0 && !kvp.Value.IsLocalPlayer)
                    {
                        kvp.Value.TransformReady = false;
                        kvp.Value.RotationReady = false;
                        kvp.Value.RealtimeEstablished = false;
                        kvp.Value.ConsecutiveErrors = 0;
                        kvp.Value.NextTransformInitTick = 0;
                        kvp.Value.NextRotationInitTick = 0;
                    }

                    kvp.Value.IsActive = true;
                    kvp.Value.IsAlive = true;
                    kvp.Value.MissingTicks = 0;

                    // Resolve TeamID lazily for observed players whose armband wasn't readable
                    // when they were first discovered. Uses exponential back-off so repeated
                    // failures don't spam scatter reads / first-chance exceptions every tick.
                    var p = kvp.Value;
                    if (!p.IsAI && p.TeamID < 0 && nowTick >= p.NextTeamIdTick)
                    {
                        bool resolved = false;
                        try
                        {
                            if (Memory.TryReadPtr(p.Base + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                                && opc.IsValidVirtualAddress()
                                && Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.InventoryController, out var invCtrl, false)
                                && invCtrl.IsValidVirtualAddress())
                            {
                                int t = GetTeamID(p, invCtrl);
                                if (t >= 0) { p.TeamID = t; resolved = true; }
                            }
                        }
                        catch { }
                        if (!resolved) ScheduleTeamIdRetry(p, nowTick);
                    }

                    // Re-evaluate teammate classification each tick — but only PROMOTE.
                    // Once a player has been classified as Teammate (because their armband
                    // matched the match-locked local team), we never demote them back to a
                    // faction type. Arena's armband reads can transiently flip during a round
                    // due to inventory rebuilds / respawns, and demoting would visibly swap
                    // teammate/enemy colors mid-match — exactly what the user reported.
                    if (!p.IsAI && p.Type != PlayerType.Teammate && p.TeamID >= 0)
                    {
                        int localTeam = _matchLocalTeamId >= 0
                            ? _matchLocalTeamId
                            : (LocalPlayer?.TeamID ?? -1);
                        if (localTeam >= 0 && p.TeamID == localTeam)
                            p.Type = PlayerType.Teammate;
                    }
                }
                else
                {
                    // Don't yank the player on the first missed tick — Arena's list read can
                    // briefly return a stale/short snapshot during respawn / heavy churn. Keep
                    // the entry alive (but inactive so the realtime worker stops scattering
                    // against possibly-freed memory) until the player has been absent for
                    // several consecutive ticks. This avoids the visible remove → re-discover
                    // gap where a still-alive enemy disappears from the overlay for ~1s+.
                    kvp.Value.IsActive = false;
                    kvp.Value.IsAlive = false;
                    if (++kvp.Value.MissingTicks >= MissingTicksBeforeRemoval)
                        (toRemove ??= []).Add(kvp.Key);
                }
            }
            if (toRemove is null) goto PublishSnapshot;
            foreach (var key in toRemove)
            {
                if (_players.TryRemove(key, out var removed))
                {
                    Log.WriteLine($"[RegisteredPlayers] Removed '{removed.Name}' ({removed.Type}) @ 0x{key:X}");
                    if (removed.IsLocalPlayer)
                    {
                        LocalPlayerLost = true;
                        LocalPlayer = null;
                        Log.WriteLine("[RegisteredPlayers] Local player lost — match likely ending.");
                    }
                }
            }

        PublishSnapshot:
            // Republish the active snapshot so the realtime worker can iterate an array
            // instead of walking the concurrent dictionary every 8ms. We allocate a fresh
            // array each tick on purpose: the realtime worker iterates the previous snapshot
            // concurrently, so reusing a backing buffer would race with its read.
            _activeList.Clear();
            foreach (var kvp in _players)
            {
                if (kvp.Value.IsActive) _activeList.Add(kvp.Value);
            }
            var snap = _activeList.Count == 0 ? _emptyPlayers : _activeList.ToArray();
            Volatile.Write(ref _activeSnapshot, snap);
        }

        // ── Realtime scatter worker ───────────────────────────────────────────

        /// <summary>
        /// Scatter-batched position + rotation reads for all active players.
        /// Called from the fast realtime worker (~8ms interval).
        /// </summary>
        internal void UpdateRealtimeData()
        {
            var active = Volatile.Read(ref _activeSnapshot);
            if (active.Length == 0) return;

            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            int readsQueued = 0;
            for (int i = 0; i < active.Length; i++)
            {
                var player = active[i];
                if (player.RotationReady)
                { scatter.PrepareReadValue<Vector2>(player.RotationAddr); readsQueued++; }
                if (player.TransformReady)
                {
                    // VerticesAddr now holds hierarchy + WorldPositionOffset directly.
                    scatter.PrepareReadValue<Vector3>(player.VerticesAddr);
                    readsQueued++;
                }
            }

            if (readsQueued == 0) return;

            scatter.Execute();

            for (int i = 0; i < active.Length; i++)
                ProcessScatterResults(scatter, active[i]);
        }

        private static void ProcessScatterResults(VmmScatter scatter, Player player)
        {
            bool rotOk = true, posOk = true;

            // Rotation
            if (player.RotationReady)
            {
                if (scatter.ReadValue<Vector2>(player.RotationAddr, out var rot)
                    && float.IsFinite(rot.X) && float.IsFinite(rot.Y))
                {
                    float yaw = rot.X % 360f;
                    if (yaw < 0f) yaw += 360f;
                    if (yaw >= 360f) yaw -= 360f; // guard against tiny negative zero -> 360 after +360
                    player.RotationYaw = yaw;
                    player.RotationPitch = rot.Y;
                }
                else rotOk = false;
            }

            // Position — single Vector3 read of the hierarchy's cached world position.
            // Sentinel <0, -1000, 0> means the player has not been placed in the scene yet
            // (freshly-spawned / respawning). That is NOT an error — just keep the last known
            // position (if any) and wait silently for the real TRS to be written.
            // An EXACT <0, 0, 0> read indicates the hierarchy memory has been zeroed/freed
            // (stale VerticesAddr after respawn or Unity tearing down the transform). That IS
            // an error — we must reinit the transform chain, otherwise the player stays "stuck"
            // at origin while actually moving in-game.
            bool sentinel = false;
            if (player.TransformReady)
            {
                if (scatter.ReadValue<Vector3>(player.VerticesAddr, out var worldPos)
                    && float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z))
                {
                    if (worldPos.Y <= -500f)
                    {
                        sentinel = true; // not spawned yet; don't touch position or error counters
                    }
                    else if (worldPos == Vector3.Zero)
                    {
                        // Hierarchy likely freed/zeroed — treat as a read error so the auto-reinit
                        // path kicks in. Don't overwrite last known good position.
                        posOk = false;
                    }
                    else
                    {
                        player.Position = worldPos;
                        player.HasValidPosition = true;
                        player.RealtimeEstablished = true;
                    }
                }
                else posOk = false;
            }
            else posOk = false;

            // Frozen-position-while-yaw-changes detector. The Unity hierarchy worldPos cache
            // can survive a respawn / round transition unchanged — the read still SUCCEEDS but
            // returns the same bit-identical Vector3 forever. Meanwhile rotation is sourced
            // from MovementContext (a separate pointer chain) and keeps refreshing. Symptom:
            // dot stuck on the map while yaw keeps spinning. When we see N consecutive ticks
            // of identical position with at least one yaw change, force a transform reinit.
            // Realtime ticks at ~8ms, so 25 ticks ≈ 200ms of provable freeze.
            if (posOk && !sentinel && player.TransformReady)
            {
                if (player.Position == player.LastObservedPosition)
                {
                    player.IdenticalPositionTicks++;
                    if (player.RotationYaw != player.LastObservedYaw)
                        player.FrozenPositionTicks++;
                }
                else
                {
                    player.IdenticalPositionTicks = 0;
                    player.FrozenPositionTicks = 0;
                    player.LastObservedPosition = player.Position;
                }
                player.LastObservedYaw = player.RotationYaw;

                const int FrozenReinitThreshold = 25;
                if (player.FrozenPositionTicks >= FrozenReinitThreshold)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"frozen_pos_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] '{player.Name}': position frozen while yaw changing for {player.FrozenPositionTicks} ticks — forcing transform reinit.");
                    player.TransformReady = false;
                    player.RealtimeEstablished = false;
                    player.ConsecutiveErrors = 0;
                    player.IdenticalPositionTicks = 0;
                    player.FrozenPositionTicks = 0;
                }
            }
            else
            {
                player.IdenticalPositionTicks = 0;
                player.FrozenPositionTicks = 0;
            }

            // Error tracking / auto-reinit — skip entirely while sentinel so we don't thrash
            // the transform chain for a player who just hasn't spawned yet.
            if (sentinel)
            {
                player.ConsecutiveErrors = 0;
            }
            else if (!rotOk || !posOk)
            {
                player.ConsecutiveErrors++;
                int threshold = player.RealtimeEstablished ? ReinitThreshold : ReinitThresholdNew;
                if (!posOk && player.TransformReady && player.ConsecutiveErrors >= threshold)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"reinit_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] Auto-invalidating transform for '{player.Name}' after {player.ConsecutiveErrors} failures");
                    player.TransformReady = false;
                    player.RotationReady = false;
                    // Release Position ownership so BatchUpdateSkeletons can drive the dot from
                    // bones while we re-resolve the transform hierarchy. Without this, the dot
                    // stays frozen at the last position for the full reinit window (often >1s).
                    player.RealtimeEstablished = false;
                    player.ConsecutiveErrors = 0;
                }
            }
            else
            {
                player.ConsecutiveErrors = 0;
            }
        }

        // ── Transform init ────────────────────────────────────────────────────

        internal void BatchInitTransforms()
        {
            long nowTick = Environment.TickCount64;
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || p.TransformReady) continue;
                if (nowTick < p.NextTransformInitTick) continue;
                if (!TryInitTransform(p))
                    ScheduleTransformInitRetry(p, nowTick);
            }
        }

        internal void BatchInitRotations()
        {
            long nowTick = Environment.TickCount64;
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || p.RotationReady) continue;
                if (nowTick < p.NextRotationInitTick) continue;
                if (!TryInitRotation(p))
                    ScheduleRotationInitRetry(p, nowTick);
            }
        }

        // ── Skeleton init + per-frame bone scatter ────────────────────────
        // Runs on the camera worker — kept off the realtime position loop so
        // skeleton reads never interfere with the primary scatter cycle.

        private readonly List<Skeleton?> _skeletonScratch = new(32);

        internal void BatchInitSkeletons()
        {
            long nowTick = Environment.TickCount64;
            int total = 0, attempted = 0, alreadyHave = 0;
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || !p.IsAlive) continue;
                if (p.IsLocalPlayer) continue;          // never draw LocalPlayer bones
                total++;
                if (p.Skeleton is not null) { alreadyHave++; continue; }
                if (nowTick < p.NextSkeletonInitTick) continue;

                attempted++;
                var sk = Skeleton.TryCreate(p.Base, isObserved: true);
                if (sk is null)
                {
                    int streak = Math.Min(++p.SkeletonInitFailStreak, 10);
                    long delayMs = Math.Min(250L << Math.Min(streak, 4), 4_000L); // 250..4000ms
                    p.NextSkeletonInitTick = nowTick + delayMs;
                }
                else
                {
                    p.Skeleton = sk;
                    p.SkeletonInitFailStreak = 0;
                    p.NextSkeletonInitTick = 0;
                }
            }

            if (total > 0)
            {
                Log.WriteRateLimited(AppLogLevel.Debug, "skel_status", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Skeletons: total={total} ready={alreadyHave} attempted={attempted}");
            }
        }

        internal void BatchUpdateSkeletons()
        {
            _skeletonScratch.Clear();
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || !p.IsAlive) continue;
                var sk = p.Skeleton;
                if (sk is null) continue;
                _skeletonScratch.Add(sk);
            }
            if (_skeletonScratch.Count == 0) return;

            try
            {
                Skeleton.UpdateBonePositionsBatched(CollectionsMarshal.AsSpan(_skeletonScratch));
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "skel_upd_err", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Skeleton batch update failed: {ex.GetType().Name}: {ex.Message}");
            }

            // Skeleton-derived position fallback: in Arena the per-player hierarchy transform
            // can be torn down across respawns/round transitions while bone hierarchies remain
            // valid. When that happens the realtime worker leaves Position at <0,0,0> and
            // Aimview filters the player out via its origin-reject gate.
            //
            // We also handle a subtler case: the realtime read can SUCCEED but return a
            // STALE cached world position (Unity's hierarchy worldPos cache isn't refreshed
            // every frame for every actor). The symptom is "rotation updates but position is
            // frozen for ~1-2s". When bones are alive and visibly diverge from the realtime
            // Position, we override from bones and invalidate the transform so the realtime
            // worker re-resolves the hierarchy on its next pass.
            //
            // The realtime worker stores Position at the rig's FEET level (hierarchy root
            // cached worldPos). Emit feet-level here too so downstream consumers (ESP box,
            // Aimview synthetic origin) don't shift vertically when the source switches.
            // Prefer the lower of the two foot bones; fall back to pelvis - 0.95m if foot
            // bones aren't ready yet.
            const float StalePositionDivergenceSq = 1.5f * 1.5f; // metres²
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || !p.IsAlive) continue;
                var sk = p.Skeleton;
                if (sk is null || !sk.IsInitialized) continue;

                Vector3? feet = null;
                var lf = sk.GetBonePosition(Bones.HumanLFoot);
                var rf = sk.GetBonePosition(Bones.HumanRFoot);
                if (lf.HasValue && rf.HasValue) feet = lf.Value.Y < rf.Value.Y ? lf : rf;
                else if (lf.HasValue) feet = lf;
                else if (rf.HasValue) feet = rf;
                else if (sk.GetBonePosition(Bones.HumanPelvis) is Vector3 pv)
                    feet = new Vector3(pv.X, pv.Y - 0.95f, pv.Z);

                if (feet is not Vector3 wp) continue;
                if (!float.IsFinite(wp.X) || !float.IsFinite(wp.Y) || !float.IsFinite(wp.Z)) continue;
                if (wp.Y <= -500f) continue;       // sentinel: not spawned
                if (wp.LengthSquared() < 1f) continue; // origin garbage

                if (!p.RealtimeEstablished)
                {
                    // Realtime hasn't taken ownership yet — bones are the only source of truth.
                    p.Position = wp;
                    p.HasValidPosition = true;
                    continue;
                }

                // Realtime owns Position — but verify it isn't stale. Compare against bones.
                var delta = wp - p.Position;
                if (delta.LengthSquared() >= StalePositionDivergenceSq)
                {
                    // Realtime read is succeeding but returning a frozen cached worldPos.
                    // Trust the bones (they're animated every frame) and invalidate the
                    // transform so the realtime worker rebuilds the hierarchy chain.
                    p.Position = wp;
                    p.HasValidPosition = true;
                    p.TransformReady = false;
                    p.RealtimeEstablished = false;
                    p.ConsecutiveErrors = 0;
                    Log.WriteRateLimited(AppLogLevel.Debug, $"stale_pos_{p.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] '{p.Name}': realtime position stale (Δ={MathF.Sqrt(delta.LengthSquared()):F1}m) — switched to bone-derived position, invalidating transform.");
                }
            }
        }

        private static bool TryInitTransform(Player player)
        {
            uint lookOffset = player.IsLocalPlayer
                ? Offsets.Player._playerLookRaycastTransform
                : Offsets.ObservedPlayerView._playerLookRaycastTransform;

            // Step 1: lookTransform managed pointer
            if (!Memory.TryReadPtr(player.Base + lookOffset, out var lookTransform, false)
                || !lookTransform.IsValidVirtualAddress())
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s1_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step1 lookTransform failed (base=0x{player.Base:X} off=0x{lookOffset:X})");
                return false;
            }

            // Step 2: +0x10 gives the C++ Transform / managed wrapper
            if (!Memory.TryReadPtr(lookTransform + 0x10, out var transformInternal, false)
                || !transformInternal.IsValidVirtualAddress())
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s2_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step2 transformInternal failed (lookTransform=0x{lookTransform:X})");
                return false;
            }

            // Step 3: resolve native TransformAccess + hierarchy in one call (no double-read race)
            if (!ResolveNativeTransformInternal(transformInternal, out var nativeTi, out var hierarchy))
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s3_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step3 nativeTI/hierarchy resolve failed (transformInternal=0x{transformInternal:X})");
                return false;
            }

            // Step 3b: index sanity (informational — kept so we still log a recognizable
            // taIndex on the OK line; not used for position lookup anymore).
            if (!Memory.TryReadValue<int>(nativeTi + TransformAccess.IndexOffset, out var taIndex, false)
                || taIndex < 0 || taIndex > 128_000)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s3b_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step3b taIndex out of range (nativeTI=0x{nativeTi:X} taIndex={taIndex})");
                return false;
            }

            // Step 4: read the cached world position at hierarchy + WorldPositionOffset.
            // In Arena Unity 6 the hierarchy stores TRS at h+0xB0/0xC0/0xD0 — no need
            // to walk a parent-index chain. Each player owns its own hierarchy.
            ulong worldPosAddr = hierarchy + TransformHierarchy.WorldPositionOffset;
            if (!Memory.TryReadValue<Vector3>(worldPosAddr, out var initPos, false)
                || !float.IsFinite(initPos.X) || !float.IsFinite(initPos.Y) || !float.IsFinite(initPos.Z))
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s4_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step4 worldPos read failed (hierarchy=0x{hierarchy:X})");
                return false;
            }

            player.TransformInternal = nativeTi;
            player.TransformIndex    = taIndex;
            player.VerticesAddr      = worldPosAddr; // realtime worker reads Vector3 here
            player.CachedIndices     = null;
            player.TransformReady    = true;
            player.TransformInitFailStreak = 0;
            player.NextTransformInitTick = 0;

            // Only apply the position if it is a real spawn (not the <0,-1000,0> sentinel and
            // not an exact <0,0,0> from a freshly-allocated / zeroed hierarchy) — otherwise the
            // player would briefly render at origin on the radar and the realtime worker would
            // flag it as "established" before the TRS has actually been written by Unity.
            if (initPos.Y > -500f && initPos != Vector3.Zero)
            {
                player.Position         = initPos;
                player.HasValidPosition = true;
            }

            Log.Write(AppLogLevel.Debug,
                $"[RegisteredPlayers] Transform OK '{player.Name}': pos={initPos} idx={taIndex}");
            return true;
        }

        /// <summary>
        /// Given a pointer read from <c>lookTransform + 0x10</c>, resolves the actual native
        /// Unity TransformInternal (TransformAccess) pointer AND the hierarchy pointer it
        /// dereferences — so the caller can use the hierarchy without a second DMA read.
        /// <para>
        /// In some Unity/IL2CPP builds <c>lookTransform + 0x10</c> is already the native pointer;
        /// in others it is a managed wrapper and the native pointer sits one more hop at <c>+0x10</c>.
        /// We detect which case applies by checking whether <c>HierarchyOffset</c> yields a valid
        /// address.  Returns false if neither candidate works.
        /// </para>
        /// </summary>
        private static bool ResolveNativeTransformInternal(
            ulong candidate,
            out ulong nativeTI,
            out ulong hierarchy)
        {
            hierarchy = 0;

            // Path A: candidate itself is the native TransformAccess object
            if (Memory.TryReadPtr(candidate + TransformAccess.HierarchyOffset, out var hA, false)
                && hA.IsValidVirtualAddress())
            {
                nativeTI  = candidate;
                hierarchy = hA;
                return true;
            }

            // Path B: candidate is a managed wrapper; native pointer is one hop deeper at +0x10
            if (Memory.TryReadPtr(candidate + 0x10, out var inner, false)
                && inner.IsValidVirtualAddress()
                && Memory.TryReadPtr(inner + TransformAccess.HierarchyOffset, out var hB, false)
                && hB.IsValidVirtualAddress())
            {
                nativeTI  = inner;
                hierarchy = hB;
                return true;
            }

            nativeTI = 0;
            return false;
        }

        // ── Rotation init ─────────────────────────────────────────────────────

        private static bool TryInitRotation(Player player)
        {
            ulong rotAddr;

            if (player.IsLocalPlayer)
            {
                // Local player: MovementContext → _rotation
                if (!Memory.TryReadPtr(player.Base + Offsets.Player.MovementContext, out var movCtx, false)
                    || !movCtx.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s1_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': movCtx failed (base=0x{player.Base:X})");
                    return false;
                }
                rotAddr = movCtx + Offsets.MovementContext._rotation;
            }
            else
            {
                // Observed: ObservedPlayerController → MovementController → rotation
                if (!Memory.TryReadPtr(player.Base + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                    || !opc.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s1_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': opc failed (base=0x{player.Base:X} off=0x{Offsets.ObservedPlayerView.ObservedPlayerController:X})");
                    return false;
                }
                if (!Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.MovementController, out var mc, false)
                    || !mc.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s2_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': mc failed (opc=0x{opc:X} off=0x{Offsets.ObservedPlayerController.MovementController:X})");
                    return false;
                }
                if (!Memory.TryReadPtr(mc + Offsets.ObservedMovementController.StateContext, out var stateCtxPtr, false)
                    || !stateCtxPtr.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s2b_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': stateCtx failed (mc=0x{mc:X} off=0x{Offsets.ObservedMovementController.StateContext:X})");
                    return false;
                }
                rotAddr = stateCtxPtr + Offsets.ObservedPlayerStateContext.Rotation;
            }

            if (!Memory.TryReadValue<Vector2>(rotAddr, out var rot, false)
                || !float.IsFinite(rot.X) || !float.IsFinite(rot.Y))
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s3_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitRotation '{player.Name}': rot read failed (rotAddr=0x{rotAddr:X})");
                return false;
            }

            player.RotationAddr  = rotAddr;
            player.RotationReady = true;
            player.RotationInitFailStreak = 0;
            player.NextRotationInitTick = 0;

            Log.Write(AppLogLevel.Debug,
                $"[RegisteredPlayers] Rotation OK '{player.Name}': yaw={rot.X:F1}°");
            return true;
        }

        // ── Discovery helpers ─────────────────────────────────────────────────

        private Player? CreatePlayerEntry(ulong playerBase, bool isLocal)
        {
            try
            {
                string name;
                string? accountId = null;
                string? profileId = null;
                PlayerType type;
                bool isAI = false;
                int teamId = -1;

                if (isLocal)
                {
                    // Arena doesn't expose a readable nickname for the local player — the Profile
                    // chain is deep and version-dependent. Use a fixed label for now.
                    name = "LocalPlayer";
                    type = PlayerType.LocalPlayer;

                    // Arena TeamID (armband color) via Player._inventoryController.
                    // We do NOT lock _matchLocalTeamId here — the very first armband read at
                    // match start is the most likely to be wrong (slot still being populated /
                    // stale ContainedItem from the reused previous-round GameWorld). Only seed
                    // an initial value; the stability gate in UpdateExistingPlayers requires N
                    // consecutive matching reads before promoting it to _matchLocalTeamId.
                    try
                    {
                        if (_matchLocalTeamId >= 0)
                        {
                            // Already locked from a previous tick (e.g. respawn rebuilds entry).
                            teamId = _matchLocalTeamId;
                        }
                        else if (Memory.TryReadPtr(playerBase + Offsets.Player._inventoryController, out var invCtrl, false)
                                 && invCtrl.IsValidVirtualAddress())
                        {
                            teamId = GetTeamIDForDiscovery(playerBase, isLocal: true, invCtrl);
                            // Note: stability streak is built/extended in UpdateExistingPlayers,
                            // not here — we don't want a single discovery-time read to count.
                        }
                    }
                    catch { }
                }
                else
                {
                    // Observed player (ObservedPlayerView hierarchy)
                    int sideRaw = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Side, false);
                    isAI = Memory.ReadValue<bool>(playerBase + Offsets.ObservedPlayerView.IsAI, false);

                    // Nickname
                    if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.NickName, out var nickPtr, false)
                        && nickPtr.IsValidVirtualAddress())
                    {
                        name = Memory.ReadUnityString(nickPtr, 64, false);
                    }
                    else
                    {
                        name = string.Empty;
                    }

                    // AccountId: not populated by Arena's server — skipped.
                    // ProfileId (optional)
                    if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ProfileId, out var profPtr, false)
                        && profPtr.IsValidVirtualAddress())
                    {
                        var prof = Memory.ReadUnityString(profPtr, 64, false);
                        if (!string.IsNullOrEmpty(prof))
                            profileId = prof;
                    }

                    // If nickname is empty, use voice-based role for AI or ID-based fallback
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (isAI)
                        {
                            var voice = TryReadVoiceLine(playerBase);
                            name = GetAIName(voice ?? string.Empty);
                        }
                        else
                        {
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = sideRaw == 4 ? $"PScav{id}" : $"PMC{id}";
                        }
                    }

                    type = isAI
                        ? GetAIType(TryReadVoiceLine(playerBase) ?? string.Empty)
                        : FactionFromSide(sideRaw);

                    // Arena TeamID (armband color) via ObservedPlayerController -> InventoryController.
                    // Only meaningful for humans (AI don't have armbands).
                    if (!isAI)
                    {
                        try
                        {
                            if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                                && opc.IsValidVirtualAddress()
                                && Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.InventoryController, out var invCtrl, false)
                                && invCtrl.IsValidVirtualAddress())
                            {
                                teamId = GetTeamIDForDiscovery(playerBase, isLocal: false, invCtrl);
                            }
                        }
                        catch { }

                        // Teammate classification — matches local player's match-locked team.
                        int localTeam = _matchLocalTeamId >= 0
                            ? _matchLocalTeamId
                            : (LocalPlayer?.TeamID ?? -1);
                        if (teamId != -1 && localTeam >= 0 && localTeam == teamId)
                            type = PlayerType.Teammate;
                    }
                }

                var player = new Player
                {
                    Base        = playerBase,
                    Name        = name,
                    AccountId   = accountId,
                    ProfileId   = profileId,
                    Type        = type,
                    IsLocalPlayer = isLocal,
                    IsAI        = isAI,
                    TeamID      = teamId,
                    IsActive    = true,
                    IsAlive     = true,
                };

                Log.WriteLine($"[RegisteredPlayers] Discovered: {player} @ 0x{playerBase:X} (local={isLocal})");

                return player;
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"create_{playerBase:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] CreatePlayerEntry FAILED 0x{playerBase:X}: {ex.Message}");
                return null;
            }
        }

        private static string? TryReadVoiceLine(ulong playerBase)
        {
            try
            {
                if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.Voice, out var voicePtr, false)
                    || !voicePtr.IsValidVirtualAddress())
                    return null;
                return Memory.ReadUnityString(voicePtr, 64, false);
            }
            catch { return null; }
        }

        // ── AI role helpers ───────────────────────────────────────────────────

        // Armband template GUID -> Arena TeamID (ArmbandColorType).
        // Note: the red/blue GUIDs are labelled from the in-game team color, which is the
        // OPPOSITE of the raw item template name (the "red armband" template is worn by the
        // in-game "blue team" and vice versa). TeamID equality is what drives teammate
        // classification, so only the label would be affected either way — these values
        // match what the player sees on-screen.
        private static readonly FrozenDictionary<string, int> _armbandTeamIds =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["63615c104bc92641374a97c8"] = (int)ArmbandColorType.red,
                ["63615bf35cb3825ded0db945"] = (int)ArmbandColorType.fuchsia,
                ["63615c36e3114462cd79f7c1"] = (int)ArmbandColorType.yellow,
                ["63615bfc5cb3825ded0db947"] = (int)ArmbandColorType.green,
                ["63615bc6ff557272023d56ac"] = (int)ArmbandColorType.azure,
                ["63615c225cb3825ded0db949"] = (int)ArmbandColorType.white,
                ["63615be82e60050cb330ef2f"] = (int)ArmbandColorType.blue,
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        /// Returns the Arena TeamID derived from the player's ArmBand slot item template GUID.
        /// -1 if not found or read fails. Caches the ArmBand slot address on the <paramref name="player"/>
        /// (if provided) so subsequent reads skip the equipment slot scan.
        /// </summary>
        private static int GetTeamID(Player? player, ulong inventoryController)
            => GetTeamIDInternal(player, inventoryController, diagBase: player?.Base ?? 0, diagIsLocal: player?.IsLocalPlayer ?? false);

        /// <summary>
        /// Discovery-time variant: lets <see cref="CreatePlayerEntry"/> pass the playerBase and
        /// isLocal flag explicitly so the diagnostic log can label the read correctly even though
        /// no Player object exists yet.
        /// </summary>
        private static int GetTeamIDForDiscovery(ulong playerBase, bool isLocal, ulong inventoryController)
            => GetTeamIDInternal(player: null, inventoryController, diagBase: playerBase, diagIsLocal: isLocal);

        private static int GetTeamIDInternal(Player? player, ulong inventoryController, ulong diagBase, bool diagIsLocal)
        {
            try
            {
                string diagLabel = diagIsLocal ? "LOCAL" : "OBS";

                // Fast path: use cached ArmBand slot ptr if we've resolved it before.
                if (player is not null && player.ArmBandSlotAddr != 0)
                {
                    int teamFast = ReadArmbandTeamFromSlotDiag(player.ArmBandSlotAddr, diagBase, diagLabel);
                    if (teamFast >= 0) return teamFast;
                    // Slot went stale (respawn / equipment rebuild) — fall through and rescan.
                    player.ArmBandSlotAddr = 0;
                }

                var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
                var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                var slots     = Memory.ReadPtr(equipment + Offsets.CompoundItem.Slots);

                using var slotsArray = MemArray<ulong>.Get(slots);
                foreach (var slotPtr in slotsArray)
                {
                    if (!slotPtr.IsValidVirtualAddress()) continue;
                    if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ID, out var slotNamePtr, false)
                        || !slotNamePtr.IsValidVirtualAddress()) continue;

                    if (Memory.ReadUnityString(slotNamePtr, 32, false) != "ArmBand") continue;

                    if (player is not null) player.ArmBandSlotAddr = slotPtr;
                    return ReadArmbandTeamFromSlotDiag(slotPtr, diagBase, diagLabel);
                }
            }
            catch { /* transient read failures are expected during player init */ }
            return -1;
        }

        // Probes the local Player object for the real <_inventoryController> field offset
        // by scanning every 8-byte aligned pointer slot in a plausible range and picking the
        // first one whose Inventory→Equipment→Slots chain contains an "ArmBand" slot whose
        // ContainedItem template GUID is a known armband GUID. Caches the resolved offset.
        // Range 0x100..0x1000 covers all observed Arena Player layouts; 8-byte aligned.
        private void ProbeLocalInventoryControllerOffset(ulong playerBase)
        {
            // Don't latch _localInvCtrlOffsetProbed=true until we actually resolve. If the
            // armband isn't equipped yet at match start, the probe legitimately fails and we
            // want to keep retrying on subsequent registration ticks until it succeeds.

            const uint scanStart = 0x100;
            const uint scanEnd   = 0x1000;
            const uint scanStep  = 0x8;

            int candidates = 0;
            for (uint off = scanStart; off < scanEnd; off += scanStep)
            {
                if (!Memory.TryReadPtr(playerBase + off, out var invCtrl, false)) continue;
                if (!invCtrl.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(invCtrl + Offsets.InventoryController.Inventory, out var inventory, false)
                    || !inventory.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(inventory + Offsets.Inventory.Equipment, out var equipment, false)
                    || !equipment.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(equipment + Offsets.CompoundItem.Slots, out var slots, false)
                    || !slots.IsValidVirtualAddress()) continue;

                MemArray<ulong>? slotsArray = null;
                try { slotsArray = MemArray<ulong>.Get(slots, false); }
                catch { continue; }
                if (slotsArray is null) continue;

                using (slotsArray)
                {
                    int slotCount = slotsArray.Count;
                    if (slotCount <= 0 || slotCount > 64) continue; // real equipment has ~10-15 slots

                    candidates++;
                    foreach (var slotPtr in slotsArray)
                    {
                        if (!slotPtr.IsValidVirtualAddress()) continue;
                        if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ID, out var slotNamePtr, false)
                            || !slotNamePtr.IsValidVirtualAddress()) continue;
                        string nm;
                        try { nm = Memory.ReadUnityString(slotNamePtr, 32, false); }
                        catch { continue; }
                        if (nm != "ArmBand") continue;

                        // Confirm by checking the ContainedItem template GUID is a known
                        // armband GUID (guards against false-positive chains).
                        int team = ReadArmbandTeamFromSlotDiag(slotPtr, playerBase, label: null);
                        if (team < 0) continue;

                        _localInvCtrlOffsetResolved = off;
                        _localInvCtrlOffsetProbed = true;
                        Log.WriteLine($"[RegisteredPlayers] LocalPlayer _inventoryController offset auto-resolved: base=0x{playerBase:X} +0x{off:X} (team={(ArmbandColorType)team}, candidates scanned={candidates})");
                        return;
                    }
                }
            }

            // Probe legitimately fails until the armband is equipped (start-of-match grace),
            // so this gets retried every team-id back-off tick. Keep it Debug + rate-limited
            // per-base so it doesn't dominate the log on every retry.
            Log.WriteRateLimited(AppLogLevel.Debug, $"local_invctrl_probe_{playerBase:X}", TimeSpan.FromSeconds(10),
                $"[RegisteredPlayers] LocalPlayer _inventoryController offset probe pending on base=0x{playerBase:X} (scanned 0x{scanStart:X}..0x{scanEnd:X}, candidates={candidates}); using hardcoded 0x{Offsets.Player._inventoryController:X}");
        }

        // Diagnostic variant: when label != null, logs the raw GUID + resolved team once per
        // (playerBase,label) pair every 30s so we can see WHY a player is being classified
        // the way they are without spamming the log.
        private static int ReadArmbandTeamFromSlotDiag(ulong slotPtr, ulong playerBase, string? label)
        {
            try
            {
                if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var containedItem, false)
                    || !containedItem.IsValidVirtualAddress())
                    return -1;

                var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                var mongo = Memory.ReadValue<Types.MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                var id = Memory.ReadUnityString(mongo.StringID, 64, false);

                if (string.IsNullOrEmpty(id)) return -1;
                bool known = _armbandTeamIds.TryGetValue(id, out var team);
                if (label is not null)
                {
                    string teamLabel = known ? ((ArmbandColorType)team).ToString() : "UNKNOWN";
                    Log.WriteRateLimited(AppLogLevel.Debug,
                        $"armband_diag_{playerBase:X}_{label}",
                        TimeSpan.FromSeconds(30),
                        $"[ArmbandDiag] {label} base=0x{playerBase:X} guid={id} -> {teamLabel}");
                }
                return known ? team : -1;
            }
            catch { return -1; }
        }

        private static PlayerType FactionFromSide(int sideRaw) => sideRaw switch
        {
            1 => PlayerType.USEC,
            2 => PlayerType.BEAR,
            4 => PlayerType.PScav,
            _ => PlayerType.Default,
        };

        // Back-off for TeamID resolution: ramp gently, capped at 5s so respawned players resolve fast.
        private static void ScheduleTeamIdRetry(Player p, long nowTick)
        {
            int streak = Math.Min(++p.TeamIdFailStreak, 10);
            long delayMs = Math.Min(150L << Math.Min(streak, 5), 5_000L); // 150ms .. 5s
            p.NextTeamIdTick = nowTick + delayMs;
        }

        // Back-off for transform/rotation init — Arena respawns are fast, keep retries snappy.
        private static void ScheduleTransformInitRetry(Player p, long nowTick)
        {
            int streak = Math.Min(++p.TransformInitFailStreak, 10);
            long delayMs = Math.Min(75L << Math.Min(streak, 4), 1_200L); // 75, 150, 300, 600, 1200ms cap
            p.NextTransformInitTick = nowTick + delayMs;
        }

        private static void ScheduleRotationInitRetry(Player p, long nowTick)
        {
            int streak = Math.Min(++p.RotationInitFailStreak, 10);
            long delayMs = Math.Min(75L << Math.Min(streak, 4), 1_200L);
            p.NextRotationInitTick = nowTick + delayMs;
        }

        private static readonly FrozenDictionary<string, (string Name, PlayerType Type)> _aiRoles =
            new Dictionary<string, (string, PlayerType)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arena_Guard_1"]  = ("Arena Guard",  PlayerType.AIGuard),
                ["Arena_Guard_2"]  = ("Arena Guard",  PlayerType.AIGuard),
                ["BossSanitar"]    = ("Sanitar",       PlayerType.AIBoss),
                ["BossBully"]      = ("Reshala",       PlayerType.AIBoss),
                ["BossGluhar"]     = ("Gluhar",        PlayerType.AIBoss),
                ["SectantPriest"]  = ("Priest",        PlayerType.AIBoss),
                ["SectantWarrior"] = ("Cultist",       PlayerType.AIRaider),
                ["BossKilla"]      = ("Killa",         PlayerType.AIBoss),
                ["BossTagilla"]    = ("Tagilla",       PlayerType.AIBoss),
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static string GetAIName(string voice)
        {
            if (_aiRoles.TryGetValue(voice, out var role)) return role.Name;
            return voice.Contains("guard", StringComparison.OrdinalIgnoreCase) ? "Guard" : "Bot";
        }

        private static PlayerType GetAIType(string voice)
        {
            if (_aiRoles.TryGetValue(voice, out var role)) return role.Type;
            return voice.Contains("guard", StringComparison.OrdinalIgnoreCase)
                ? PlayerType.AIGuard
                : PlayerType.AIScav;
        }
    }
}
