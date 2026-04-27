using eft_dma_radar.Arena.DMA;
using eft_dma_radar.Arena.GameWorld.Players;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.IL2CPP;
using SDK;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

using static eft_dma_radar.Arena.Unity.UnityOffsets;

namespace eft_dma_radar.Arena.GameWorld
{
    internal sealed partial class RegisteredPlayers
    {

        // ΓöÇΓöÇ Realtime scatter worker ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

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
            // An EXACT <0, 0, 0> read has two interpretations:
            //  • !RealtimeEstablished (new entry, hierarchy not yet populated): treat as sentinel —
            //    the hierarchy is still being initialised; waiting silently is correct.
            //  • RealtimeEstablished (we previously had a real position): treat as error — the
            //    hierarchy was likely freed/zeroed after a respawn and must be re-resolved.
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
                        if (!player.RealtimeEstablished)
                        {
                            // Hierarchy not yet populated for a new/respawning entry — treat
                            // identically to the -1000 sentinel: wait silently, no error.
                            sentinel = true;
                        }
                        else
                        {
                            // Previously had a valid position but now reads zero — hierarchy
                            // was likely freed/zeroed after a respawn. Treat as an error so
                            // the auto-reinit path kicks in.
                            posOk = false;
                        }
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
            else if (!posOk)
            {
                // Only position failures drive ConsecutiveErrors / auto-reinit.
                // Rotation failures are independent: the rotation address can briefly be
                // unreadable while position is fine (StateContext rebuild during a respawn),
                // and counting those against the position threshold caused phantom large
                // ConsecutiveErrors counts that instantly invalidated the transform on the
                // very first real position failure.
                if (player.TransformReady)
                {
                    player.ConsecutiveErrors++;
                    int threshold = player.RealtimeEstablished ? ReinitThreshold : ReinitThresholdNew;
                    if (player.ConsecutiveErrors >= threshold)
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
                        // The bone hierarchy is co-located with the transform; after a pointer churn
                        // or Unity TRS reset the old VerticesAddr/indices are stale. Clear the
                        // skeleton so BatchInitSkeletons re-resolves it cleanly instead of rendering
                        // bones that were read from a freed / recycled memory block.
                        player.Skeleton = null;
                        player.SkeletonInitFailStreak = 0;
                        player.NextSkeletonInitTick = Environment.TickCount64 + 250;
                    }
                }
            }
            else
            {
                // Position succeeded — clear the error counter regardless of rotation state.
                player.ConsecutiveErrors = 0;
            }
        }

        // ΓöÇΓöÇ Transform init ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

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

        // ΓöÇΓöÇ Skeleton init + per-frame bone scatter ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ
        // Runs on the camera worker ΓÇö kept off the realtime position loop so
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
            const float StalePositionDivergenceSq = 1.5f * 1.5f; // metres┬▓
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
                    // Realtime hasn't taken ownership yet ΓÇö bones are the only source of truth.
                    p.Position = wp;
                    p.HasValidPosition = true;
                    continue;
                }

                // Realtime owns Position ΓÇö but verify it isn't stale. Compare against bones.
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
                        $"[RegisteredPlayers] '{p.Name}': realtime position stale (╬ö={MathF.Sqrt(delta.LengthSquared()):F1}m) ΓÇö switched to bone-derived position, invalidating transform.");
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

            // Dump the lookTransform managed object in debug mode only — avoids ArgumentOutOfRangeException
            // storms from ReadUnityString during IL2CPP field enumeration in normal play.
            if (Log.EnableDebugLogging)
                Il2CppDumper.DumpClassFields(lookTransform, $"TryInitTransform.LookTransform '{player.Name}' @ 0x{player.Base:X}");

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

            // Step 3b: index sanity (informational ΓÇö kept so we still log a recognizable
            // taIndex on the OK line; not used for position lookup anymore).
            if (!Memory.TryReadValue<int>(nativeTi + TransformAccess.IndexOffset, out var taIndex, false)
                || taIndex < 0 || taIndex > 128_000)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"tx_s3b_{player.Base:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] TryInitTransform '{player.Name}': step3b taIndex out of range (nativeTI=0x{nativeTi:X} taIndex={taIndex})");
                return false;
            }

            // Step 4: read the cached world position at hierarchy + WorldPositionOffset.
            // In Arena Unity 6 the hierarchy stores TRS at h+0xB0/0xC0/0xD0 ΓÇö no need
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
            // After a successful transform re-init, allow the skeleton to retry promptly.
            // If the skeleton was cleared during an auto-invalidation its streak may have been
            // reset already; if it still has an old streak from a previous discovery cycle,
            // clearing it here ensures the bone chain is re-resolved within one registration tick.
            if (player.Skeleton is null)
            {
                player.SkeletonInitFailStreak = 0;
                player.NextSkeletonInitTick   = Environment.TickCount64 + 150;
            }

            // Only apply the position if it is a real spawn (not the <0,-1000,0> sentinel and
            // not an exact <0,0,0> from a freshly-allocated / zeroed hierarchy) ΓÇö otherwise the
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
        /// dereferences ΓÇö so the caller can use the hierarchy without a second DMA read.
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

        // ΓöÇΓöÇ Rotation init ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

        private static bool TryInitRotation(Player player)
        {
            ulong rotAddr;

            if (player.IsLocalPlayer)
            {
                // Local player: MovementContext ΓåÆ _rotation
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
                // Observed: ObservedPlayerController ΓåÆ MovementController ΓåÆ rotation
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
                $"[RegisteredPlayers] Rotation OK '{player.Name}': yaw={rot.X:F1}┬░");
            return true;
        }

    }
}
