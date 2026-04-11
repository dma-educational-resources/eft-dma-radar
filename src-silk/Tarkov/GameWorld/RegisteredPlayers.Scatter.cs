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
            entry.Player.RotationPitch = rotation.Y;
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
        /// On change: uses fast re-init from cached TransformInternal (skips first 6 hops).
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
                    // Fast re-init: TransformInternal is stable — only Hierarchy→Vertices/Indices changed.
                    // This saves 6 DMA reads vs full TryInitTransform (which re-walks from playerBase).
                    if (TryReinitFromTransformInternal(entry))
                    {
                        Log.WriteLine($"[RegisteredPlayers] Transform changed for '{entry.Player.Name}' — fast re-init OK (verts 0x{entry.VerticesAddr:X})");
                    }
                    else
                    {
                        // Fast path failed — fall back to full re-init
                        Log.WriteLine($"[RegisteredPlayers] Transform changed for '{entry.Player.Name}' — fast re-init failed, full re-init");
                        entry.TransformReady = false;
                        entry.TransformInitFailures = 0;
                        entry.NextTransformRetry = default;
                        TryInitTransform(entry.Base, entry);
                    }
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
        /// Fast re-init path: re-reads Hierarchy → Vertices/Indices from a cached TransformInternal
        /// pointer. Saves 6 DMA reads vs full TryInitTransform. Used when ValidateTransforms detects
        /// a vertices change but TransformInternal itself is still valid.
        /// </summary>
        private static bool TryReinitFromTransformInternal(PlayerEntry entry)
        {
            try
            {
                var ti = entry.TransformInternal;
                if (ti == 0)
                    return false;

                var taIndex = Memory.ReadValue<int>(ti + TransformAccess.IndexOffset, false);
                var taHierarchy = Memory.ReadPtr(ti + TransformAccess.HierarchyOffset, false);

                if (taIndex < 0 || taIndex > 128_000 || !taHierarchy.IsValidVirtualAddress())
                    return false;

                var verticesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.VerticesOffset, false);
                var indicesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.IndicesOffset, false);

                if (!verticesAddr.IsValidVirtualAddress() || !indicesAddr.IsValidVirtualAddress())
                    return false;

                int count = taIndex + 1;
                var indices = Memory.ReadArray<int>(indicesAddr, count, false);
                var testVertices = Memory.ReadArray<TrsX>(verticesAddr, count, false);

                if (testVertices is null || !TestPositionCompute(taIndex, indices, testVertices))
                    return false;

                // Commit — TransformInternal stays the same, only refresh downstream data
                entry.TransformIndex = taIndex;
                entry.VerticesAddr = verticesAddr;
                entry.CachedIndices = indices;
                entry.TransformReady = true;
                return true;
            }
            catch
            {
                return false;
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

        #region Batched Init (Scatter)

        /// <summary>
        /// Reusable list for collecting entries that need transform/rotation init.
        /// Avoids per-call allocation in the registration worker loop.
        /// </summary>
        private readonly List<PlayerEntry> _batchInitEntries = new(MaxPlayerCount);

        // Reusable buffers for BatchInitTransforms — avoids 11 heap arrays per call
        private ulong[] _btiBodyPtrs = [];
        private ulong[] _btiSkelPtrs = [];
        private ulong[] _btiDizValuesPtrs = [];
        private ulong[] _btiArrPtrs = [];
        private ulong[] _btiBonePtrs = [];
        private ulong[] _btiTransformInternals = [];
        private int[] _btiTaIndices = [];
        private ulong[] _btiHierarchyPtrs = [];
        private ulong[] _btiVerticesPtrs = [];
        private ulong[] _btiIndicesPtrs = [];
        private bool[] _btiValid = [];

        // Reusable buffers for BatchInitRotations — avoids 5 heap arrays per call
        private ulong[] _birOpcPtrs = [];
        private ulong[] _birMcStep1 = [];
        private ulong[] _birMcFinal = [];
        private ulong[] _birRotAddrs = [];
        private bool[] _birValid = [];

        /// <summary>
        /// Ensures a reusable array is at least <paramref name="minLength"/> long, then clears it.
        /// Only reallocates when the buffer is too small — amortized zero-alloc.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureBuffer(ref ulong[] buffer, int minLength)
        {
            if (buffer.Length < minLength)
                buffer = new ulong[minLength];
            else
                Array.Clear(buffer, 0, minLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureBuffer(ref int[] buffer, int minLength)
        {
            if (buffer.Length < minLength)
                buffer = new int[minLength];
            else
                Array.Clear(buffer, 0, minLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureBuffer(ref bool[] buffer, int minLength, bool fillValue = false)
        {
            if (buffer.Length < minLength)
                buffer = new bool[minLength];
            if (fillValue)
                Array.Fill(buffer, true, 0, minLength);
            else
                Array.Clear(buffer, 0, minLength);
        }

        /// <summary>
        /// Batched scatter-based initialization of transforms and rotations for all entries
        /// that need it. Replaces per-player serial <see cref="TryInitTransform"/> calls
        /// (N × 8 serial DMA reads) with 8 scatter rounds (batching all players in each round).
        /// <para>
        /// Called from the registration worker thread after player discovery and before
        /// <see cref="UpdateExistingPlayers"/>. Handles both new entries (failures=0) and
        /// retries (with exponential backoff).
        /// </para>
        /// </summary>
        private void BatchInitTransformsAndRotations()
        {
            var now = DateTime.UtcNow;
            long swStart = Stopwatch.GetTimestamp();

            // Count totals for summary
            int totalPlayers = _players.Count;
            int alreadyTransformReady = 0;
            int alreadyRotationReady = 0;
            int transformMaxedOut = 0;
            int rotationMaxedOut = 0;

            // Collect entries needing transform init
            _batchInitEntries.Clear();
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (entry.TransformReady)
                    alreadyTransformReady++;
                else if (entry.TransformInitFailures >= MaxInitRetries)
                    transformMaxedOut++;
                else if (now >= entry.NextTransformRetry)
                    _batchInitEntries.Add(entry);
            }

            int transformCandidates = _batchInitEntries.Count;
            int transformSucceeded = 0;

            if (_batchInitEntries.Count > 0)
            {
                if (_batchInitEntries.Count == 1)
                {
                    // Single entry — use the serial path (no scatter overhead)
                    var e = _batchInitEntries[0];
                    TryInitTransform(e.Base, e);
                    UpdateInitBackoff(e, e.TransformReady, isTransform: true, now);
                    if (e.TransformReady) transformSucceeded = 1;
                }
                else
                {
                    BatchInitTransforms(_batchInitEntries, now);
                    foreach (var e in _batchInitEntries)
                        if (e.TransformReady) transformSucceeded++;
                }
            }

            // Collect entries needing rotation init
            _batchInitEntries.Clear();
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (entry.RotationReady)
                    alreadyRotationReady++;
                else if (entry.RotationInitFailures >= MaxInitRetries)
                    rotationMaxedOut++;
                else if (now >= entry.NextRotationRetry)
                    _batchInitEntries.Add(entry);
            }

            int rotationCandidates = _batchInitEntries.Count;
            int rotationSucceeded = 0;

            if (_batchInitEntries.Count > 0)
            {
                if (_batchInitEntries.Count == 1)
                {
                    var e = _batchInitEntries[0];
                    TryInitRotation(e.Base, e);
                    UpdateInitBackoff(e, e.RotationReady, isTransform: false, now);
                    if (e.RotationReady) rotationSucceeded = 1;
                }
                else
                {
                    BatchInitRotations(_batchInitEntries, now);
                    foreach (var e in _batchInitEntries)
                        if (e.RotationReady) rotationSucceeded++;
                }
            }

            // Assign spawn-groups for newly initialized human players
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (entry.TransformReady && entry.Player.IsHuman
                    && entry.Player.SpawnGroupID == -1 && !entry.Player.IsLocalPlayer)
                {
                    entry.Player.SpawnGroupID = GetOrAssignSpawnGroup(entry.Player.Position);
                }
            }

            // Always-visible summary when there was work to do
            var elapsed = Stopwatch.GetElapsedTime(swStart);
            if (transformCandidates > 0 || rotationCandidates > 0)
            {
                Log.WriteLine($"[RegisteredPlayers] BatchInit: {totalPlayers} players, " +
                    $"transform({transformCandidates} candidates, {transformSucceeded} OK, {alreadyTransformReady} already, {transformMaxedOut} maxed), " +
                    $"rotation({rotationCandidates} candidates, {rotationSucceeded} OK, {alreadyRotationReady} already, {rotationMaxedOut} maxed), " +
                    $"elapsed={elapsed.TotalMilliseconds:F1}ms");
            }
        }

        /// <summary>Counts <c>true</c> values in a bool span — avoids LINQ delegate allocation.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountTrue(bool[] arr, int length)
        {
            int c = 0;
            for (int i = 0; i < length; i++)
                if (arr[i]) c++;
            return c;
        }

        /// <summary>
        /// Scatter-batched transform initialization for multiple entries.
        /// Walks the 8-hop pointer chain across all entries simultaneously using VmmScatter rounds.
        /// </summary>
        private void BatchInitTransforms(List<PlayerEntry> entries, DateTime now)
        {
            int n = entries.Count;

            // Reuse pre-allocated buffers — only reallocates when player count grows
            EnsureBuffer(ref _btiBodyPtrs, n);
            EnsureBuffer(ref _btiSkelPtrs, n);
            EnsureBuffer(ref _btiDizValuesPtrs, n);
            EnsureBuffer(ref _btiArrPtrs, n);
            EnsureBuffer(ref _btiBonePtrs, n);
            EnsureBuffer(ref _btiTransformInternals, n);
            EnsureBuffer(ref _btiTaIndices, n);
            EnsureBuffer(ref _btiHierarchyPtrs, n);
            EnsureBuffer(ref _btiVerticesPtrs, n);
            EnsureBuffer(ref _btiIndicesPtrs, n);
            EnsureBuffer(ref _btiValid, n, fillValue: true);

            var bodyPtrs = _btiBodyPtrs;
            var skelPtrs = _btiSkelPtrs;
            var dizValuesPtrs = _btiDizValuesPtrs;
            var arrPtrs = _btiArrPtrs;
            var bonePtrs = _btiBonePtrs;
            var transformInternals = _btiTransformInternals;
            var taIndices = _btiTaIndices;
            var hierarchyPtrs = _btiHierarchyPtrs;
            var verticesPtrs = _btiVerticesPtrs;
            var indicesPtrs = _btiIndicesPtrs;
            var valid = _btiValid;

            int validCount;

            // Round 1: Read PlayerBody
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    uint bodyOffset = entry.IsObserved
                        ? Offsets.ObservedPlayerView.PlayerBody
                        : Offsets.Player._playerBody;
                    scatter.PrepareReadValue<ulong>(entry.Base + bodyOffset);
                }
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    uint bodyOffset = entry.IsObserved
                        ? Offsets.ObservedPlayerView.PlayerBody
                        : Offsets.Player._playerBody;
                    if (scatter.ReadValue<ulong>(entry.Base + bodyOffset, out var body) && body.IsValidVirtualAddress())
                        bodyPtrs[i] = body;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R1 (PlayerBody): {validCount}/{n} valid");

            // Round 2: Read SkeletonRootJoint
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<ulong>(bodyPtrs[i] + Offsets.PlayerBody.SkeletonRootJoint);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    if (scatter.ReadValue<ulong>(bodyPtrs[i] + Offsets.PlayerBody.SkeletonRootJoint, out var skel) && skel.IsValidVirtualAddress())
                        skelPtrs[i] = skel;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R2 (SkeletonRootJoint): {validCount}/{n} valid");

            // Round 3: Read _values
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<ulong>(skelPtrs[i] + Offsets.DizSkinningSkeleton._values);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    if (scatter.ReadValue<ulong>(skelPtrs[i] + Offsets.DizSkinningSkeleton._values, out var diz) && diz.IsValidVirtualAddress())
                        dizValuesPtrs[i] = diz;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R3 (_values): {validCount}/{n} valid");

            // Round 4: Read arr (List backing array)
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<ulong>(dizValuesPtrs[i] + List.ArrOffset);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    if (scatter.ReadValue<ulong>(dizValuesPtrs[i] + List.ArrOffset, out var arr) && arr.IsValidVirtualAddress())
                        arrPtrs[i] = arr;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R4 (ListArr): {validCount}/{n} valid");

            // Round 5: Read bone[0] entry pointer
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<ulong>(arrPtrs[i] + List.ArrStartOffset);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    if (scatter.ReadValue<ulong>(arrPtrs[i] + List.ArrStartOffset, out var bone) && bone.IsValidVirtualAddress())
                        bonePtrs[i] = bone;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R5 (Bone0): {validCount}/{n} valid");

            // Round 6: Read TransformInternal from bone entry
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<ulong>(bonePtrs[i] + 0x10);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    if (scatter.ReadValue<ulong>(bonePtrs[i] + 0x10, out var ti) && ti.IsValidVirtualAddress())
                        transformInternals[i] = ti;
                    else
                        valid[i] = false;
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R6 (TransformInternal): {validCount}/{n} valid");

            // Round 7: Read taIndex + taHierarchy from TransformInternal (both from same base)
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    scatter.PrepareReadValue<int>(transformInternals[i] + TransformAccess.IndexOffset);
                    scatter.PrepareReadValue<ulong>(transformInternals[i] + TransformAccess.HierarchyOffset);
                }
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    bool idxOk = scatter.ReadValue<int>(transformInternals[i] + TransformAccess.IndexOffset, out var idx);
                    bool hierOk = scatter.ReadValue<ulong>(transformInternals[i] + TransformAccess.HierarchyOffset, out var hier);

                    if (idxOk && hierOk && idx >= 0 && idx <= 128_000 && hier.IsValidVirtualAddress())
                    {
                        taIndices[i] = idx;
                        hierarchyPtrs[i] = hier;
                    }
                    else
                    {
                        valid[i] = false;
                    }
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R7 (Index+Hierarchy): {validCount}/{n} valid");

            // Round 8: Read vertices + indices from hierarchy (both from same base)
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    scatter.PrepareReadValue<ulong>(hierarchyPtrs[i] + TransformHierarchy.VerticesOffset);
                    scatter.PrepareReadValue<ulong>(hierarchyPtrs[i] + TransformHierarchy.IndicesOffset);
                }
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i]) continue;
                    bool vOk = scatter.ReadValue<ulong>(hierarchyPtrs[i] + TransformHierarchy.VerticesOffset, out var verts);
                    bool iOk = scatter.ReadValue<ulong>(hierarchyPtrs[i] + TransformHierarchy.IndicesOffset, out var inds);

                    if (vOk && iOk && verts.IsValidVirtualAddress() && inds.IsValidVirtualAddress())
                    {
                        verticesPtrs[i] = verts;
                        indicesPtrs[i] = inds;
                    }
                    else
                    {
                        valid[i] = false;
                    }
                }
            }
            validCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransforms R8 (Vertices+Indices): {validCount}/{n} valid");

            // Final: read indices array + test vertex data for each valid entry (serial per entry — variable size)
            for (int i = 0; i < n; i++)
            {
                var entry = entries[i];
                if (!valid[i])
                {
                    UpdateInitBackoff(entry, success: false, isTransform: true, now);
                    continue;
                }

                try
                {
                    int count = taIndices[i] + 1;
                    var indices = Memory.ReadArray<int>(indicesPtrs[i], count, false);
                    var testVertices = Memory.ReadArray<TrsX>(verticesPtrs[i], count, false);

                    if (testVertices is null || !TestPositionCompute(taIndices[i], indices, testVertices))
                    {
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransform '{entry.Player.Name}': " +
                            $"pointer chain OK but vertex data not ready (idx={taIndices[i]}, verts=0x{verticesPtrs[i]:X})");
                        UpdateInitBackoff(entry, success: false, isTransform: true, now);
                        continue;
                    }

                    entry.TransformInternal = transformInternals[i];
                    entry.TransformIndex = taIndices[i];
                    entry.VerticesAddr = verticesPtrs[i];
                    entry.CachedIndices = indices;
                    entry.TransformReady = true;

                    if (entry.TransformInitFailures > 0)
                        Log.WriteLine($"[RegisteredPlayers] BatchInitTransform OK '{entry.Player.Name}' after {entry.TransformInitFailures} prior failures");
                    entry.TransformInitFailures = 0;

                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitTransform OK '{entry.Player.Name}': " +
                        $"transformInternal=0x{transformInternals[i]:X}, idx={taIndices[i]}, verts=0x{verticesPtrs[i]:X}");
                }
                catch
                {
                    UpdateInitBackoff(entry, success: false, isTransform: true, now);
                }
            }

            int finalOk = 0;
            int chainOkButVertexFail = 0;
            for (int i2 = 0; i2 < n; i2++)
            {
                if (entries[i2].TransformReady) finalOk++;
                if (valid[i2] && !entries[i2].TransformReady) chainOkButVertexFail++;
            }
            Log.WriteLine($"[RegisteredPlayers] BatchInitTransforms DONE: {n} entries, {finalOk} succeeded, " +
                $"{n - validCount} chain-failed, {chainOkButVertexFail} chain-ok-but-vertex-fail");
        }

        /// <summary>
        /// Scatter-batched rotation initialization for multiple entries.
        /// Observed: OPC → MovementController chain → rotation addr (3 hops).
        /// Client: MovementContext → rotation addr (1 hop).
        /// </summary>
        private void BatchInitRotations(List<PlayerEntry> entries, DateTime now)
        {
            int n = entries.Count;

            // Reuse pre-allocated buffers — only reallocates when player count grows
            EnsureBuffer(ref _birOpcPtrs, n);
            EnsureBuffer(ref _birMcStep1, n);
            EnsureBuffer(ref _birMcFinal, n);
            EnsureBuffer(ref _birRotAddrs, n);
            EnsureBuffer(ref _birValid, n, fillValue: true);

            var opcPtrs = _birOpcPtrs;       // Observed: OPC; Client: MovementContext
            var mcStep1 = _birMcStep1;       // Observed: OPC+0xD8; Client: unused
            var mcFinal = _birMcFinal;       // Observed: step1+0x98; Client: unused
            var rotAddrs = _birRotAddrs;
            var valid = _birValid;

            int rValidCount;

            // Round 1: Read OPC (observed) or MovementContext (client)
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    uint offset = entry.IsObserved
                        ? Offsets.ObservedPlayerView.ObservedPlayerController
                        : Offsets.Player.MovementContext;
                    scatter.PrepareReadValue<ulong>(entry.Base + offset);
                }
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    uint offset = entry.IsObserved
                        ? Offsets.ObservedPlayerView.ObservedPlayerController
                        : Offsets.Player.MovementContext;
                    if (scatter.ReadValue<ulong>(entry.Base + offset, out var ptr) && ptr.IsValidVirtualAddress())
                    {
                        opcPtrs[i] = ptr;
                        // Client players are done — rotation addr is MovementContext + _rotation
                        if (!entry.IsObserved)
                            rotAddrs[i] = ptr + Offsets.MovementContext._rotation;
                    }
                    else
                    {
                        valid[i] = false;
                    }
                }
            }
            rValidCount = CountTrue(valid, n);
            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitRotations R1 (OPC/MovCtx): {rValidCount}/{n} valid");

            // Round 2: Observed only — read MovementController step 1 (OPC + 0xD8)
            bool anyObserved = false;
            for (int i = 0; i < n; i++)
                if (valid[i] && entries[i].IsObserved) { anyObserved = true; break; }

            if (anyObserved)
            {
                using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);
                for (int i = 0; i < n; i++)
                    if (valid[i] && entries[i].IsObserved)
                        scatter.PrepareReadValue<ulong>(opcPtrs[i] + Offsets.ObservedPlayerController.MovementController[0]);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i] || !entries[i].IsObserved) continue;
                    if (scatter.ReadValue<ulong>(opcPtrs[i] + Offsets.ObservedPlayerController.MovementController[0], out var mc1) && mc1.IsValidVirtualAddress())
                        mcStep1[i] = mc1;
                    else
                        valid[i] = false;
                }
                rValidCount = CountTrue(valid, n);
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitRotations R2 (MC step1): {rValidCount}/{n} valid");

                // Round 3: Observed only — read MovementController step 2 (step1 + 0x98)
                using var scatter2 = Memory.GetScatter(VmmFlags.NOCACHE);
                for (int i = 0; i < n; i++)
                    if (valid[i] && entries[i].IsObserved)
                        scatter2.PrepareReadValue<ulong>(mcStep1[i] + Offsets.ObservedPlayerController.MovementController[1]);
                scatter2.Execute();
                for (int i = 0; i < n; i++)
                {
                    if (!valid[i] || !entries[i].IsObserved) continue;
                    if (scatter2.ReadValue<ulong>(mcStep1[i] + Offsets.ObservedPlayerController.MovementController[1], out var mc2) && mc2.IsValidVirtualAddress())
                    {
                        mcFinal[i] = mc2;
                        rotAddrs[i] = mc2 + Offsets.ObservedMovementController.Rotation;
                    }
                    else
                    {
                        valid[i] = false;
                    }
                }
                rValidCount = CountTrue(valid, n);
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitRotations R3 (MC step2): {rValidCount}/{n} valid");
            }

            // Final round: Read rotation value to validate it's sane
            using (var scatter = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < n; i++)
                    if (valid[i]) scatter.PrepareReadValue<Vector2>(rotAddrs[i]);
                scatter.Execute();
                for (int i = 0; i < n; i++)
                {
                    var entry = entries[i];
                    if (!valid[i])
                    {
                        UpdateInitBackoff(entry, success: false, isTransform: false, now);
                        continue;
                    }

                    if (scatter.ReadValue<Vector2>(rotAddrs[i], out var rot)
                        && float.IsFinite(rot.X) && float.IsFinite(rot.Y))
                    {
                        entry.RotationAddr = rotAddrs[i];
                        entry.RotationReady = true;

                        if (entry.RotationInitFailures > 0)
                            Log.WriteLine($"[RegisteredPlayers] BatchInitRotation OK '{entry.Player.Name}' after {entry.RotationInitFailures} prior failures");
                        entry.RotationInitFailures = 0;

                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] BatchInitRotation OK '{entry.Player.Name}': " +
                            $"rotAddr=0x{rotAddrs[i]:X}, rot=({rot.X:F1}, {rot.Y:F1})");
                    }
                    else
                    {
                        UpdateInitBackoff(entry, success: false, isTransform: false, now);
                    }
                }
            }

            int rotOk = 0;
            for (int i = 0; i < n; i++)
                if (entries[i].RotationReady) rotOk++;
            Log.WriteLine($"[RegisteredPlayers] BatchInitRotations DONE: {n} entries, {rotOk} succeeded");
        }

        /// <summary>
        /// Updates backoff state for a failed init attempt (shared by batch and serial paths).
        /// </summary>
        private static void UpdateInitBackoff(PlayerEntry entry, bool success, bool isTransform, DateTime now)
        {
            if (success)
                return;

            if (isTransform)
            {
                entry.TransformInitFailures++;
                double backoffSec = entry.TransformInitFailures switch
                {
                    1 => 0.1,
                    2 => 0.2,
                    3 => 0.5,
                    _ => Math.Min(entry.TransformInitFailures * 0.5, 2.0)
                };
                entry.NextTransformRetry = now.AddSeconds(backoffSec);
            }
            else
            {
                entry.RotationInitFailures++;
                double backoffSec = entry.RotationInitFailures switch
                {
                    1 => 0.1,
                    2 => 0.2,
                    3 => 0.5,
                    _ => Math.Min(entry.RotationInitFailures * 0.5, 2.0)
                };
                entry.NextRotationRetry = now.AddSeconds(backoffSec);
            }
        }

        #endregion
    }
}
