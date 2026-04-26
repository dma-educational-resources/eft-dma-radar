using System.Collections.Concurrent;
using System.Collections.Frozen;
using eft_dma_radar.Arena.DMA;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.Collections;
using eft_dma_radar.Arena.Unity.IL2CPP;
using SDK;
using VmmSharpEx.Options;

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
    internal sealed partial class RegisteredPlayers
    {
        #region Constants

        private const int MaxPlayerCount       = 64;
        // Arena rounds churn pointers fast â€” invalidate sooner so retries actually run again.
        private const int ReinitThreshold      = 3;
        private const int ReinitThresholdNew   = 2;

        // Grace period (in registration ticks, ~100ms each) before an observed player who has
        // dropped out of the RegisteredPlayers list is actually removed. Arena's list-read can
        // momentarily return a stale/short snapshot during fast churn / respawns, and many
        // first-chance BadPtr/Vmm exceptions are recoverable. Keeping the entry around for a
        // few extra ticks lets the next tick re-confirm the player instead of churning
        // remove â†’ re-discover (which makes them disappear from the overlay for ~1s+).
        private const int MissingTicksBeforeRemoval = 5; // ~500ms

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;

        private readonly ConcurrentDictionary<ulong, Player> _players = new();
        private readonly HashSet<ulong> _seenSet = new(MaxPlayerCount);
        private readonly List<Player> _activeList = new(MaxPlayerCount);

        // Snapshot of active players, rebuilt by the registration worker after each tick.
        // The realtime worker reads this via Volatile â€” no per-tick dictionary enumeration.
        private static readonly Player[] _emptyPlayers = [];
        private Player[] _activeSnapshot = _emptyPlayers;

        private int _invalidCountStreak;

        // Team identity is locked at match start. The first time the local player's armband
        // resolves to a valid TeamID we snapshot it here and reuse it for the remainder of the
        // match â€” across deaths, respawns, and MainPlayer pointer flips. This prevents teammate
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
        // so it cannot be used to detect match end â€” only an empty RegisteredPlayers list can.
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

        /// <summary>
        /// Dumps the full IL2CPP hierarchy for every currently active player.
        /// Called by <see cref="LocalGameWorld.DumpAll"/> when debug logging is toggled on.
        /// </summary>
        internal void DumpAll()
        {
            foreach (var p in _players.Values)
            {
                if (!p.IsActive) continue;
                try { DumpPlayerHierarchy(p.Base, p.Name, p.IsLocalPlayer); }
                catch (Exception ex)
                {
                    Log.WriteLine($"[RegisteredPlayers] DumpAll failed for '{p.Name}': {ex.Message}");
                }
            }
        }

        // â”€â”€ Registration worker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Discovers (or re-discovers) the local (MainPlayer) instance from the GameWorld.MainPlayer
        /// pointer. Arena reuses the same GameWorld across deaths/respawns but swaps the MainPlayer
        /// pointer each round, so this is called every registration tick â€” not just once â€” and will
        /// replace the cached entry whenever the pointer changes.
        /// Returns true when a valid local player is registered.
        /// </summary>
        internal bool TryDiscoverLocalPlayer()
        {
            if (!Memory.TryReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayerPtr, false)
                || !mainPlayerPtr.IsValidVirtualAddress())
            {
                // MainPlayer temporarily null â€” this happens on every death/respawn in Arena
                // (the GameWorld is reused across rounds and between deaths). Keep the existing
                // LocalPlayer entry but mark it inactive so the realtime worker stops scattering
                // against a stale ptr. Do NOT trip LocalPlayerLost here â€” match-end is signalled
                // authoritatively by the RegisteredPlayers list going empty (see RefreshRegistration).
                if (LocalPlayer is not null)
                {
                    LocalPlayer.IsActive = false;
                }
                return LocalPlayer is not null;
            }

            // Same pointer as before â†’ nothing to do, just make sure it's marked active.
            if (LocalPlayer is not null && LocalPlayerAddr == mainPlayerPtr)
            {
                LocalPlayer.IsActive = true;
                LocalPlayer.IsAlive = true;
                return true;
            }

            // Pointer changed (respawn / next round) or first discovery â€” build a fresh entry.
            var player = CreatePlayerEntry(mainPlayerPtr, isLocal: true);
            if (player is null)
                return LocalPlayer is not null;

            // Drop the old local entry (if any) â€” its transform/rotation addrs are now stale.
            if (LocalPlayer is not null && LocalPlayerAddr != 0
                && _players.TryRemove(LocalPlayerAddr, out var oldLocal))
            {
                Log.WriteLine($"[RegisteredPlayers] LocalPlayer pointer changed 0x{LocalPlayerAddr:X} -> 0x{mainPlayerPtr:X} (respawn)");
                // Tell the camera worker the FPS camera was almost certainly rebuilt by
                // the respawn â€” bypass its normal 500ms rate-limit so ESP/Aimview don't
                // render against a stale matrix until the next routine refresh.
                CameraManager.RequestFpsCameraRefresh();
            }

            LocalPlayer = player;
            LocalPlayerAddr = mainPlayerPtr;
            _players[mainPlayerPtr] = player;
            Log.WriteLine($"[RegisteredPlayers] LocalPlayer: {player}");
            if (Log.EnableDebugLogging)
            {
                Il2CppDumper.DumpClassFields(mainPlayerPtr, $"LocalPlayer (EFT.Player) @ 0x{mainPlayerPtr:X}");
                // Dump MovementContext sub-object for rotation chain verification
                if (Memory.TryReadPtr(mainPlayerPtr + Offsets.Player.MovementContext, out var mc, false) && mc.IsValidVirtualAddress())
                    Il2CppDumper.DumpClassFields(mc, $"LocalPlayer MovementContext @ 0x{mc:X}");
            }
            return true;
        }

        /// <summary>
        /// Refreshes the registered player list â€” discovers new, removes gone.
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
                        Log.WriteLine($"[RegisteredPlayers] RegisteredPlayers list empty for {_invalidCountStreak} ticks â€” match ended.");
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
            // While not yet locked, keep sampling the local armband each (back-off) tick â€” even
            // if LocalPlayer.TeamID is already set â€” and require N consecutive identical reads
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

                    // Hardcoded offset failed to produce a known armband â€” kick off (or retry) the probe.
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
                            // Probe failed (armband not equipped yet) â€” back off so we don't
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

                    // Re-evaluate teammate classification each tick â€” but only PROMOTE.
                    // Once a player has been classified as Teammate (because their armband
                    // matched the match-locked local team), we never demote them back to a
                    // faction type. Arena's armband reads can transiently flip during a round
                    // due to inventory rebuilds / respawns, and demoting would visibly swap
                    // teammate/enemy colors mid-match â€” exactly what the user reported.
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
                    // Don't yank the player on the first missed tick â€” Arena's list read can
                    // briefly return a stale/short snapshot during respawn / heavy churn. Keep
                    // the entry alive (but inactive so the realtime worker stops scattering
                    // against possibly-freed memory) until the player has been absent for
                    // several consecutive ticks. This avoids the visible remove â†’ re-discover
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
                        Log.WriteLine("[RegisteredPlayers] Local player lost â€” match likely ending.");
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

        // ── Shared back-off schedulers ────────────────────────────────────────
        // Called across all partial files via partial class visibility.

        // Back-off for TeamID resolution: ramp gently, capped at 5s so respawned players resolve fast.
        private static void ScheduleTeamIdRetry(Player p, long nowTick)
        {
            int streak = Math.Min(++p.TeamIdFailStreak, 10);
            long delayMs = Math.Min(150L << Math.Min(streak, 5), 5_000L); // 150ms .. 5s
            p.NextTeamIdTick = nowTick + delayMs;
        }

        // Back-off for transform/rotation init â€” Arena respawns are fast, keep retries snappy.
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

    }
}
