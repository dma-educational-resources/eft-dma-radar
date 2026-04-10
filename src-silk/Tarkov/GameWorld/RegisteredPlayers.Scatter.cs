using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    internal sealed partial class RegisteredPlayers
    {
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

            // --- Error state with debounce + recovery hysteresis ---
            bool tickFailed = !rotOk || !posOk;
            if (tickFailed)
            {
                entry.RecoveryCount = 0;
                entry.ConsecutiveErrors++;

                // Only enter error state for players that previously had a valid position.
                // Players that never had a valid position (just spawned, game data not ready yet)
                // are in a "warming up" state — the registration worker will keep retrying.
                if (entry.ConsecutiveErrors >= ErrorThreshold && !entry.HasError && entry.Player.HasValidPosition)
                {
                    entry.HasError = true;
                    entry.Player.IsError = true;
                    Log.WriteLine($"[RegisteredPlayers] Player '{entry.Player.Name}' entered error state after {entry.ConsecutiveErrors} consecutive failures (rot={rotOk}, pos={posOk})");
                }

                // If position keeps failing despite TransformReady, the pointer chain data may not
                // be populated yet (e.g., player just spawned). Invalidate the transform so the
                // registration worker re-walks the pointer chain with fresh data.
                if (!posOk && entry.TransformReady && entry.ConsecutiveErrors >= ReinitThreshold)
                {
                    Log.WriteLine($"[RegisteredPlayers] Auto-invalidating transform for '{entry.Player.Name}' after {entry.ConsecutiveErrors} consecutive position failures");
                    entry.TransformReady = false;
                    entry.TransformInitFailures = 0;
                    entry.NextTransformRetry = default;
                    entry.ConsecutiveErrors = 0; // Reset so we don't immediately re-trigger
                }
            }
            else
            {
                entry.ConsecutiveErrors = 0;
                if (entry.HasError)
                {
                    entry.RecoveryCount++;
                    if (entry.RecoveryCount >= RecoveryThreshold)
                    {
                        Log.WriteLine($"[RegisteredPlayers] Player '{entry.Player.Name}' recovered from error state");
                        entry.RecoveryCount = 0;
                        entry.HasError = false;
                        entry.Player.IsError = false;
                    }
                }
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
                var worldPos = vertices[entry.TransformIndex].T;
                int idx = indices[entry.TransformIndex];
                int iterations = 0;

                while (idx >= 0)
                {
                    if (iterations++ > MaxHierarchyIterations)
                        return false;

                    var parent = vertices[idx];
                    worldPos = Vector3.Transform(worldPos, parent.Q);
                    worldPos *= parent.S;
                    worldPos += parent.T;

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
                var arrPtr = Memory.ReadPtr(dizValues + List.ArrOffset, false);
                var boneEntryPtr = Memory.ReadPtr(arrPtr + List.ArrStartOffset, false);
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

                // Validate that position data is actually readable before committing.
                // The pointer chain can be valid while the game hasn't populated vertex data yet
                // (e.g., player just spawned). Without this check, TransformReady=true but every
                // position read fails until the game finishes initialization.
                var testVertices = Memory.ReadArray<TrsX>(verticesAddr, count, false);
                if (testVertices is null || !TestPositionCompute(taIndex, indices, testVertices))
                {
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitTransform '{entry.Player.Name}' 0x{playerBase:X}: " +
                        $"pointer chain OK but vertex data not ready (idx={taIndex}, verts=0x{verticesAddr:X})");
                    return; // Leave TransformReady=false — registration worker will retry
                }

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

        /// <summary>
        /// Tests whether vertex data produces a valid world position (no side effects).
        /// Used during TryInitTransform to verify game data is actually populated.
        /// </summary>
        private static bool TestPositionCompute(int transformIndex, int[] indices, TrsX[] vertices)
        {
            try
            {
                var worldPos = vertices[transformIndex].T;
                int idx = indices[transformIndex];
                int iterations = 0;

                while (idx >= 0)
                {
                    if (iterations++ > MaxHierarchyIterations)
                        return false;

                    var parent = vertices[idx];
                    worldPos = Vector3.Transform(worldPos, parent.Q);
                    worldPos *= parent.S;
                    worldPos += parent.T;

                    idx = indices[idx];
                }

                return float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z)
                    && (worldPos.X != 0f || worldPos.Y != 0f || worldPos.Z != 0f);
            }
            catch
            {
                return false;
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
                    rotAddr = mc + Offsets.ObservedMovementController.Rotation;
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitRotation '{entry.Player.Name}': observed opc=0x{opc:X} mc=0x{mc:X} rotAddr=0x{rotAddr:X}");
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + Offsets.MovementContext._rotation;
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
    }
}
