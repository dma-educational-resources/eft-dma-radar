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
        private const int MaxHierarchyIter     = 4000;
        private const int ErrorThreshold       = 3;
        private const int RecoveryThreshold    = 2;
        private const int ReinitThreshold      = 5;
        private const int ReinitThresholdNew   = 2;

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;

        private readonly ConcurrentDictionary<ulong, Player> _players = new();
        private readonly HashSet<ulong> _seenSet = new(MaxPlayerCount);
        private readonly List<Player> _activeList = new(MaxPlayerCount);

        private int _invalidCountStreak;
        private int _mainPlayerNullStreak;

        // Arena respawns are fast but not instant; allow MainPlayer to be null for a handful of
        // registration ticks (~100ms each) before declaring the local player truly lost.
        private const int MainPlayerNullTicksBeforeLost = 50; // ~5s at 100ms tick

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
                // MainPlayer temporarily null (e.g. between rounds / during respawn). Keep the
                // existing LocalPlayer but mark it inactive so the realtime worker stops
                // scattering against a stale ptr. Only after a sustained null streak do we treat
                // the session as truly over.
                if (LocalPlayer is not null)
                {
                    LocalPlayer.IsActive = false;
                    if (++_mainPlayerNullStreak >= MainPlayerNullTicksBeforeLost)
                    {
                        Log.WriteLine($"[RegisteredPlayers] MainPlayer null for {_mainPlayerNullStreak} ticks — local player lost.");
                        LocalPlayerLost = true;
                        LocalPlayer = null;
                        LocalPlayerAddr = 0;
                    }
                }
                return LocalPlayer is not null;
            }

            _mainPlayerNullStreak = 0;

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
                if (count < 1 || count > MaxPlayerCount)
                {
                    _invalidCountStreak++;
                    Log.WriteRateLimited(AppLogLevel.Warning, "rp_count", TimeSpan.FromSeconds(10),
                        $"[RegisteredPlayers] Invalid player count: {count} (addr=0x{listAddr:X}), streak={_invalidCountStreak}");

                    // Arena note: the RegisteredPlayers list can legitimately be empty between
                    // rounds / during respawn while the GameWorld is reused. Do NOT flag
                    // LocalPlayerLost here — that is handled by TryDiscoverLocalPlayer() based on
                    // sustained MainPlayer==null, which is the authoritative match-ended signal.
                    if (_invalidCountStreak > 3)
                        Thread.Sleep(Math.Min(1000 * _invalidCountStreak, 10_000));
                    return;
                }

                _invalidCountStreak = 0;

                var seen = _seenSet;
                seen.Clear();
                seen.EnsureCapacity(count);

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
                    kvp.Value.IsActive = true;
                    kvp.Value.IsAlive = true;
                }
                else
                {
                    kvp.Value.IsActive = false;
                    kvp.Value.IsAlive = false;
                    (toRemove ??= []).Add(kvp.Key);
                }
            }
            if (toRemove is null) return;
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
        }

        // ── Realtime scatter worker ───────────────────────────────────────────

        /// <summary>
        /// Scatter-batched position + rotation reads for all active players.
        /// Called from the fast realtime worker (~8ms interval).
        /// </summary>
        internal void UpdateRealtimeData()
        {
            if (_players.IsEmpty)
                return;

            _activeList.Clear();
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive) continue;
                _activeList.Add(p);
            }
            if (_activeList.Count == 0) return;

            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            int readsQueued = 0;
            foreach (var player in _activeList)
            {
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

            foreach (var player in _activeList)
                ProcessScatterResults(scatter, player);
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

                    if (player.IsLocalPlayer)
                    {
                        Log.WriteRateLimited(AppLogLevel.Debug, "local_rot_live", TimeSpan.FromMilliseconds(500),
                            $"[RegisteredPlayers] LocalPlayer live rot: X={rot.X:F2} Y={rot.Y:F2} (addr=0x{player.RotationAddr:X})");
                    }
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
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || p.TransformReady) continue;
                TryInitTransform(p);
            }
        }

        internal void BatchInitRotations()
        {
            foreach (var kvp in _players)
            {
                var p = kvp.Value;
                if (!p.IsActive || p.RotationReady) continue;
                TryInitRotation(p);
            }
        }

        private static void TryInitTransform(Player player)
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
                return;
            }

            // Step 2: +0x10 gives the C++ Transform / managed wrapper
            if (!Memory.TryReadPtr(lookTransform + 0x10, out var transformInternal, false)
                || !transformInternal.IsValidVirtualAddress())
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s2_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step2 transformInternal failed (lookTransform=0x{lookTransform:X})");
                return;
            }

            // Step 3: resolve native TransformAccess + hierarchy in one call (no double-read race)
            if (!ResolveNativeTransformInternal(transformInternal, out var nativeTi, out var hierarchy))
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s3_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step3 nativeTI/hierarchy resolve failed (transformInternal=0x{transformInternal:X})");
                // Run the chain dump so we can see the raw layout
                if (transformInternal.IsValidVirtualAddress())
                    DumpTransformChainToLog(player, transformInternal);
                return;
            }

            // Step 3b: index sanity (informational — kept so we still log a recognizable
            // taIndex on the OK line; not used for position lookup anymore).
            if (!Memory.TryReadValue<int>(nativeTi + TransformAccess.IndexOffset, out var taIndex, false)
                || taIndex < 0 || taIndex > 128_000)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s3b_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step3b taIndex out of range (nativeTI=0x{nativeTi:X} taIndex={taIndex})");
                DumpTransformChainToLog(player, nativeTi);
                return;
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
                DumpTransformChainToLog(player, nativeTi, hierarchy);
                return;
            }

            player.TransformInternal = nativeTi;
            player.TransformIndex    = taIndex;
            player.VerticesAddr      = worldPosAddr; // realtime worker reads Vector3 here
            player.CachedIndices     = null;
            player.TransformReady    = true;

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

        // ── Transform dump ────────────────────────────────────────────────────

        // Keys of players already dumped — one dump per player per process lifetime is enough.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, byte> _dumpedPlayers = new();

        /// <summary>
        /// Dumps 128 bytes of <paramref name="nativeTI"/> (and optionally a hierarchy object)
        /// into the Warning log, once per player address lifetime.
        /// </summary>
        private static void DumpTransformChainToLog(Player player, ulong nativeTI, ulong hierarchy = 0)
        {
            if (!_dumpedPlayers.TryAdd(player.Base, 0))
                return;

            var sb = new System.Text.StringBuilder(2048);
            sb.AppendLine($"[TransformDump] '{player.Name}' base=0x{player.Base:X} nativeTI=0x{nativeTI:X}");

            DumpObject(sb, "nativeTI", nativeTI);

            // Explicit key-offset reads
            void LogField(ulong baseAddr, string label, uint offset)
            {
                if (Memory.TryReadValue<ulong>(baseAddr + offset, out var val, false))
                    sb.AppendLine($"  {label} (+0x{offset:X2}) = 0x{val:X16}  masked=0x{val & ~(ulong)0x3F:X16}  direct={val.IsValidVirtualAddress()}  masked={(val & ~(ulong)0x3F).IsValidVirtualAddress()}");
                else
                    sb.AppendLine($"  {label} (+0x{offset:X2}) = <read failed>");
            }

            LogField(nativeTI, "Hierarchy ", TransformAccess.HierarchyOffset);
            LogField(nativeTI, "Index(int)", TransformAccess.IndexOffset);

            // If caller provided a resolved hierarchy, dump it directly; otherwise try to read it
            ulong hAddr = hierarchy;
            if (hAddr == 0 && Memory.TryReadValue<ulong>(nativeTI + TransformAccess.HierarchyOffset, out var hRaw, false))
            {
                hAddr = hRaw.IsValidVirtualAddress() ? hRaw : hRaw & ~(ulong)0x3F;
                if (!hAddr.IsValidVirtualAddress()) hAddr = 0;
            }

            if (hAddr != 0)
            {
                sb.AppendLine($"  -- hierarchy @ 0x{hAddr:X} --");
                DumpObject(sb, "hierarchy ", hAddr);
                foreach (uint off in new uint[] { 0x10, 0x18, 0x20, 0x28, 0x30, 0x38, 0x40, 0x48, 0x50, 0x58, 0x60, 0x68, 0x70, 0x78, 0x80 })
                    LogField(hAddr, $"  h+0x{off:X2}  ", off);
            }

            Log.Write(AppLogLevel.Warning, sb.ToString());
        }

        private static void DumpObject(System.Text.StringBuilder sb, string label, ulong addr)
        {
            try
            {
                var buf = Memory.ReadArray<byte>(addr, 128, false);
                if (buf is { Length: > 0 })
                {
                    sb.AppendLine($"  [{label} @ 0x{addr:X}]");
                    for (int row = 0; row < 128; row += 16)
                    {
                        sb.Append($"    +0x{row:X3}:  ");
                        for (int col = 0; col < 16 && row + col < buf.Length; col++)
                            sb.Append($"{buf[row + col]:X2} ");
                        sb.AppendLine();
                    }
                }
                else sb.AppendLine($"  [{label}] ReadArray returned null/empty");
            }
            catch (Exception ex) { sb.AppendLine($"  [{label}] hex dump failed: {ex.Message}"); }
        }

        private static void TryInitRotation(Player player)
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
                    return;
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
                    return;
                }
                if (!Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.MovementController, out var mc, false)
                    || !mc.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s2_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': mc failed (opc=0x{opc:X} off=0x{Offsets.ObservedPlayerController.MovementController:X})");
                    return;
                }
                if (!Memory.TryReadPtr(mc + Offsets.ObservedMovementController.StateContext, out var stateCtxPtr, false)
                    || !stateCtxPtr.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s2b_{player.Base:X}", TimeSpan.FromSeconds(5),
                        $"[RegisteredPlayers] TryInitRotation '{player.Name}': stateCtx failed (mc=0x{mc:X} off=0x{Offsets.ObservedMovementController.StateContext:X})");
                    return;
                }
                rotAddr = stateCtxPtr + Offsets.ObservedPlayerStateContext.Rotation;
            }

            if (!Memory.TryReadValue<Vector2>(rotAddr, out var rot, false)
                || !float.IsFinite(rot.X) || !float.IsFinite(rot.Y))
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"rot_s3_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitRotation '{player.Name}': rot read failed (rotAddr=0x{rotAddr:X})");
                return;
            }

            player.RotationAddr  = rotAddr;
            player.RotationReady = true;

            Log.Write(AppLogLevel.Debug,
                $"[RegisteredPlayers] Rotation OK '{player.Name}': yaw={rot.X:F1}°");
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

                if (isLocal)
                {
                    // Local player — read directly from its own Profile (not yet needed for Arena,
                    // so we do a best-effort read and fall back to "LocalPlayer").
                    name = TryReadLocalPlayerName(playerBase) ?? "LocalPlayer";
                    type = PlayerType.LocalPlayer;
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
                            name = GetAIName(voice ?? string.Empty, _mapId);
                        }
                        else
                        {
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = sideRaw == 4 ? $"PScav{id}" : $"PMC{id}";
                        }
                    }

                    type = isAI
                        ? GetAIType(TryReadVoiceLine(playerBase) ?? string.Empty)
                        : sideRaw switch
                        {
                            1 => PlayerType.USEC,
                            2 => PlayerType.BEAR,
                            4 => PlayerType.PScav,
                            _ => PlayerType.Default,
                        };
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

        private static string? TryReadLocalPlayerName(ulong playerBase)
        {
            try
            {
                // Arena client players share EFT.Player layout — Profile is at a large offset.
                // For now just use a simple fallback; the name will appear in Arena UI later.
                // Profile chain is deep and version-dependent, so we skip it here.
                return null;
            }
            catch { return null; }
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

        private static string GetAIName(string voice, string mapId)
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
