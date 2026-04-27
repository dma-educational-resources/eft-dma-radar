using System.Buffers;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.IL2CPP;
using SDK;
using VmmSharpEx;
using VmmSharpEx.Options;

using static eft_dma_radar.Arena.Unity.UnityOffsets;

namespace eft_dma_radar.Arena.GameWorld.Players
{
    /// <summary>
    /// Per-player skeleton — resolves the bone pointer chain, batches per-bone
    /// world-position reads via DMA scatter, and projects bones to screen space
    /// for the aimview widget.
    /// <para>
    /// Arena's player hierarchy caches the root's world position at
    /// <c>hierarchy + 0xB0</c> (see <see cref="TransformHierarchy.WorldPositionOffset"/>),
    /// but individual bones live inside the hierarchy arrays and therefore still
    /// require the parent-index walk exactly like EFT-silk.
    /// </para>
    /// <para>
    /// The aimview widget consumes <see cref="ScreenBuffer"/> (26 points = 13 line
    /// segments × 2 endpoints), which is populated by
    /// <see cref="UpdateScreenBuffer"/> using <see cref="CameraManager.WorldToScreen"/>.
    /// </para>
    /// </summary>
    internal sealed class Skeleton
    {
        /// <summary>Number of line-segment endpoints in the screen buffer (13 × 2).</summary>
        public const int JOINTS_COUNT = 26;

        /// <summary>All 16 skeleton bones used for drawing (mirrors silk order).</summary>
        private static readonly Bones[] _allBones =
        [
            Bones.HumanHead,
            Bones.HumanNeck,
            Bones.HumanSpine3,
            Bones.HumanSpine2,
            Bones.HumanSpine1,
            Bones.HumanPelvis,
            Bones.HumanLCollarbone,
            Bones.HumanRCollarbone,
            Bones.HumanLForearm2,
            Bones.HumanRForearm2,
            Bones.HumanLPalm,
            Bones.HumanRPalm,
            Bones.HumanLThigh2,
            Bones.HumanRThigh2,
            Bones.HumanLFoot,
            Bones.HumanRFoot,
        ];

        private readonly BoneEntry[] _bones;
        private readonly Dictionary<Bones, int> _boneIndex;

        /// <summary>26 points forming 13 line segments. Written by UpdateScreenBuffer.</summary>
        public readonly Vector2[] ScreenBuffer = new Vector2[JOINTS_COUNT];

        /// <summary>Whether the screen buffer contains valid data for the current frame.</summary>
        public volatile bool HasScreenData;

        /// <summary>Whether at least one bone has a valid world position.</summary>
        public volatile bool IsInitialized;

        private sealed class BoneEntry
        {
            public readonly Bones Bone;
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public int TransformIndex;
            public int[]? CachedIndices;
            public bool Ready;

            public Vector3 WorldPosition;
            public bool HasPosition;

            public BoneEntry(Bones bone) => Bone = bone;
        }

        private Skeleton(BoneEntry[] bones)
        {
            _bones = bones;
            _boneIndex = new Dictionary<Bones, int>(bones.Length);
            for (int i = 0; i < bones.Length; i++)
                _boneIndex[bones[i].Bone] = i;
        }

        /// <summary>
        /// Walks the bone pointer chain for the given player. Returns null if the
        /// chain is unreadable (player data not yet fully initialized).
        /// Chain: playerBase → PlayerBody → SkeletonRootJoint → DizSkinningSkeleton._values →
        /// List._items → element[boneIdx] → +0x10 → TransformInternal.
        /// </summary>
        internal static Skeleton? TryCreate(ulong playerBase, bool isObserved)
        {
            try
            {
                if (!isObserved)
                    return null;

                uint playerBodyOffset = Offsets.ObservedPlayerView.PlayerBody;

                // Step 1 — PlayerBody pointer
                if (!Memory.TryReadPtr(playerBase + playerBodyOffset, out var playerBody, false))
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"skel_s1_{playerBase:X}", TimeSpan.FromSeconds(10),
                        $"[Skeleton] step1 FAIL: PlayerBody ptr unreadable at base=0x{playerBase:X} + 0x{playerBodyOffset:X}");
                    return null;
                }

                if (Log.EnableDebugLogging)
                    Il2CppDumper.DumpClassFields(playerBody, $"Skeleton.PlayerBody @ 0x{playerBase:X}");
                // Step 2 — SkeletonRootJoint pointer
                if (!Memory.TryReadPtr(playerBody + Offsets.PlayerBody.SkeletonRootJoint, out var skeletonRoot, false))
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"skel_s2_{playerBase:X}", TimeSpan.FromSeconds(10),
                        $"[Skeleton] step2 FAIL: SkeletonRootJoint unreadable at PlayerBody=0x{playerBody:X} + 0x{Offsets.PlayerBody.SkeletonRootJoint:X}");
                    return null;
                }

                // Dump the DizSkinningSkeleton only in debug mode — avoids ReadUnityString
                // ArgumentOutOfRangeException storms during normal play.
                if (Log.EnableDebugLogging)
                {
                    Il2CppDumper.DumpClassFields(playerBody, $"Skeleton.PlayerBody @ 0x{playerBase:X}");
                    Il2CppDumper.DumpClassFields(skeletonRoot, $"Skeleton.DizSkinningSkeleton @ 0x{playerBase:X}");
                }

                // Step 3 — DizSkinningSkeleton._values pointer (List<TransformComponent>)
                if (!Memory.TryReadPtr(skeletonRoot + Offsets.DizSkinningSkeleton._values, out var values, false))
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"skel_s3_{playerBase:X}", TimeSpan.FromSeconds(10),
                        $"[Skeleton] step3 FAIL: DizSkinningSkeleton._values unreadable at SkeletonRoot=0x{skeletonRoot:X} + 0x{Offsets.DizSkinningSkeleton._values:X}");
                    return null;
                }

                // Step 4 — List backing array pointer (values + 0x10 → array header)
                if (!Memory.TryReadPtr(values + List.ArrOffset, out var itemsArr, false) ||
                    !itemsArr.IsValidVirtualAddress())
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"skel_s4_{playerBase:X}", TimeSpan.FromSeconds(10),
                        $"[Skeleton] step4 FAIL: List.ArrOffset unreadable at values=0x{values:X} + 0x{List.ArrOffset:X} (got itemsArr=0x{itemsArr:X})");
                    return null;
                }

                var entries = new BoneEntry[_allBones.Length];
                for (int i = 0; i < _allBones.Length; i++)
                {
                    var bone = _allBones[i];
                    entries[i] = new BoneEntry(bone);

                    try
                    {
                        // element addr: itemsArr + 0x20 + boneIdx * 8
                        ulong elemAddr = itemsArr + List.ArrStartOffset + (uint)bone * 0x8u;
                        if (!Memory.TryReadPtr(elemAddr, out var transformComponent, false) ||
                            !Memory.TryReadPtr(transformComponent + 0x10, out var transformCandidate, false) ||
                            !transformCandidate.IsValidVirtualAddress())
                        {
                            continue;
                        }

                        // Resolve native TransformAccess → TransformHierarchy. Path A: candidate is
                        // already native; Path B: candidate is a managed wrapper with the native
                        // pointer at +0x10.
                        ulong transformInternal = 0;
                        ulong taHierarchy = 0;
                        bool hasA = Memory.TryReadPtr(transformCandidate + TransformAccess.HierarchyOffset, out var hA, false) &&
                                    hA.IsValidVirtualAddress();
                        if (hasA)
                        {
                            transformInternal = transformCandidate;
                            taHierarchy = hA;
                        }
                        else if (Memory.TryReadPtr(transformCandidate + 0x10, out var inner, false) &&
                                 inner.IsValidVirtualAddress() &&
                                 Memory.TryReadPtr(inner + TransformAccess.HierarchyOffset, out var hB, false) &&
                                 hB.IsValidVirtualAddress())
                        {
                            transformInternal = inner;
                            taHierarchy = hB;
                        }
                        else
                        {
                            continue;
                        }

                        if (!Memory.TryReadValue<int>(transformInternal + TransformAccess.IndexOffset, out var taIndex, false) ||
                            taIndex < 0 || taIndex > 128_000)
                            continue;

                        if (!Memory.TryReadPtr(taHierarchy + TransformHierarchy.VerticesOffset, out var verticesAddr, false) ||
                            !Memory.TryReadPtr(taHierarchy + TransformHierarchy.IndicesOffset, out var indicesAddr, false) ||
                            !verticesAddr.IsValidVirtualAddress() ||
                            !indicesAddr.IsValidVirtualAddress())
                            continue;

                        int count = taIndex + 1;
                        int[] indices;
                        try { indices = Memory.ReadArray<int>(indicesAddr, count, false); }
                        catch { continue; }

                        entries[i].TransformInternal = transformInternal;
                        entries[i].TransformIndex = taIndex;
                        entries[i].VerticesAddr = verticesAddr;
                        entries[i].CachedIndices = indices;
                        entries[i].Ready = true;
                    }
                    catch
                    {
                        // Individual bone failure — leave entry not-ready
                    }
                }

                // Need at least the Spine2 (mid-torso) anchor and a handful of bones
                int readyCount = 0;
                bool hasAnchor = false;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!entries[i].Ready) continue;
                    readyCount++;
                    if (entries[i].Bone == Bones.HumanSpine2) hasAnchor = true;
                }
                if (!hasAnchor || readyCount < 4)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, $"skel_tc_{playerBase:X}", TimeSpan.FromSeconds(10),
                        $"[Skeleton] TryCreate failed — anchor={hasAnchor}, ready={readyCount}/{entries.Length} (base=0x{playerBase:X})");
                    return null;
                }

                Log.Write(AppLogLevel.Info,
                    $"[Skeleton] Created — {readyCount}/{entries.Length} bones ready (base=0x{playerBase:X})");
                return new Skeleton(entries) { IsInitialized = true };
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Debug, $"skel_ex_{playerBase:X}", TimeSpan.FromSeconds(10),
                    $"[Skeleton] TryCreate exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Batches world-position reads for multiple skeletons in a single DMA scatter.
        /// Called from the camera worker to avoid N per-skeleton scatter cycles.
        /// </summary>
        internal static void UpdateBonePositionsBatched(ReadOnlySpan<Skeleton?> skeletons)
        {
            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            for (int s = 0; s < skeletons.Length; s++)
            {
                var skeleton = skeletons[s];
                if (skeleton is null) continue;
                var bones = skeleton._bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    var entry = bones[i];
                    if (!entry.Ready) continue;
                    scatter.PrepareReadArray<TrsX>(entry.VerticesAddr, entry.TransformIndex + 1);
                }
            }

            scatter.Execute();

            for (int s = 0; s < skeletons.Length; s++)
            {
                var skeleton = skeletons[s];
                if (skeleton is null) continue;
                var bones = skeleton._bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    var entry = bones[i];
                    if (!entry.Ready) continue;

                    int vcount = entry.TransformIndex + 1;
                    var rented = ArrayPool<TrsX>.Shared.Rent(vcount);
                    try
                    {
                        var vertices = rented.AsSpan(0, vcount);
                        if (!scatter.ReadSpan<TrsX>(entry.VerticesAddr, vertices))
                            continue;

                        var indices = entry.CachedIndices;
                        if (indices is null || entry.TransformIndex >= indices.Length)
                            continue;

                        var worldPos = TrsX.ComputeWorldPosition(vertices, indices, entry.TransformIndex);
                        if (float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z))
                        {
                            entry.WorldPosition = worldPos;
                            entry.HasPosition = true;
                        }
                    }
                    finally
                    {
                        ArrayPool<TrsX>.Shared.Return(rented);
                    }
                }
            }
        }

        /// <summary>
        /// Projects all bone world positions through <see cref="CameraManager.WorldToScreen"/>
        /// and fills <see cref="ScreenBuffer"/> with 26 points (13 line segments).
        /// Returns true if at least a torso-ish anchor projected successfully.
        /// </summary>
        internal bool UpdateScreenBuffer(Vector2 contentMin, int widgetW, int widgetH)
        {
            var head   = ProjectBone(Bones.HumanHead,        contentMin, widgetW, widgetH);
            var neck   = ProjectBone(Bones.HumanNeck,        contentMin, widgetW, widgetH);
            var upper  = ProjectBone(Bones.HumanSpine3,      contentMin, widgetW, widgetH);
            var mid    = ProjectBone(Bones.HumanSpine2,      contentMin, widgetW, widgetH);
            var lower  = ProjectBone(Bones.HumanSpine1,      contentMin, widgetW, widgetH);
            var pelvis = ProjectBone(Bones.HumanPelvis,      contentMin, widgetW, widgetH);

            var lCollar = ProjectBone(Bones.HumanLCollarbone, contentMin, widgetW, widgetH);
            var rCollar = ProjectBone(Bones.HumanRCollarbone, contentMin, widgetW, widgetH);
            var lElbow  = ProjectBone(Bones.HumanLForearm2,   contentMin, widgetW, widgetH);
            var rElbow  = ProjectBone(Bones.HumanRForearm2,   contentMin, widgetW, widgetH);
            var lHand   = ProjectBone(Bones.HumanLPalm,       contentMin, widgetW, widgetH);
            var rHand   = ProjectBone(Bones.HumanRPalm,       contentMin, widgetW, widgetH);

            var lKnee = ProjectBone(Bones.HumanLThigh2, contentMin, widgetW, widgetH);
            var rKnee = ProjectBone(Bones.HumanRThigh2, contentMin, widgetW, widgetH);
            var lFoot = ProjectBone(Bones.HumanLFoot,   contentMin, widgetW, widgetH);
            var rFoot = ProjectBone(Bones.HumanRFoot,   contentMin, widgetW, widgetH);

            Vector2? anchor = mid ?? upper ?? lower ?? pelvis ?? neck ?? head;
            if (!anchor.HasValue)
            {
                HasScreenData = false;
                return false;
            }

            var a = anchor.Value;
            int idx = 0;
            // Head → neck → upper → mid → lower → pelvis
            ScreenBuffer[idx++] = head   ?? a; ScreenBuffer[idx++] = neck  ?? a;
            ScreenBuffer[idx++] = neck   ?? a; ScreenBuffer[idx++] = upper ?? a;
            ScreenBuffer[idx++] = upper  ?? a; ScreenBuffer[idx++] = a;
            ScreenBuffer[idx++] = a;           ScreenBuffer[idx++] = lower ?? a;
            ScreenBuffer[idx++] = lower  ?? a; ScreenBuffer[idx++] = pelvis ?? a;

            // Pelvis → left knee → left foot
            ScreenBuffer[idx++] = pelvis ?? a; ScreenBuffer[idx++] = lKnee ?? a;
            ScreenBuffer[idx++] = lKnee  ?? a; ScreenBuffer[idx++] = lFoot ?? a;

            // Pelvis → right knee → right foot
            ScreenBuffer[idx++] = pelvis ?? a; ScreenBuffer[idx++] = rKnee ?? a;
            ScreenBuffer[idx++] = rKnee  ?? a; ScreenBuffer[idx++] = rFoot ?? a;

            // Left collar → left elbow → left hand
            ScreenBuffer[idx++] = lCollar ?? a; ScreenBuffer[idx++] = lElbow ?? a;
            ScreenBuffer[idx++] = lElbow  ?? a; ScreenBuffer[idx++] = lHand  ?? a;

            // Right collar → right elbow → right hand
            ScreenBuffer[idx++] = rCollar ?? a; ScreenBuffer[idx++] = rElbow ?? a;
            ScreenBuffer[idx++] = rElbow  ?? a; ScreenBuffer[idx++] = rHand  ?? a;

            HasScreenData = true;
            return true;
        }

        /// <summary>World position of a specific bone (null if unavailable).</summary>
        internal Vector3? GetBonePosition(Bones bone)
        {
            if (_boneIndex.TryGetValue(bone, out int idx))
            {
                var entry = _bones[idx];
                if (entry.HasPosition) return entry.WorldPosition;
            }
            return null;
        }

        /// <summary>
        /// Synthetic-mode (yaw/pitch basis) variant of <see cref="UpdateScreenBuffer"/>.
        /// Used when <see cref="CameraManager"/> can't supply a usable view matrix
        /// (e.g. <c>VM.T==0</c>); projects bones via the same forward/right/up basis
        /// the Aimview synthetic dot uses, so bones stay aligned with the dot.
        /// </summary>
        internal bool UpdateScreenBufferSynthetic(
            Vector3 eyePos, Vector3 forward, Vector3 right, Vector3 up, float zoom,
            Vector2 contentMin, int widgetW, int widgetH)
        {
            float halfW = widgetW * 0.5f;
            float halfH = widgetH * 0.5f;

            Vector2? P(Bones bone)
            {
                if (!_boneIndex.TryGetValue(bone, out int idx)) return null;
                var e = _bones[idx];
                if (!e.HasPosition) return null;
                var dir = e.WorldPosition - eyePos;
                float dz = Vector3.Dot(dir, forward);
                if (dz <= 0.05f) return null;
                float dx = Vector3.Dot(dir, right);
                float dy = Vector3.Dot(dir, up);
                float nx = dx / dz * zoom;
                float ny = dy / dz * zoom;
                return new Vector2(
                    contentMin.X + halfW + nx * halfW,
                    contentMin.Y + halfH - ny * halfH);
            }

            var head   = P(Bones.HumanHead);
            var neck   = P(Bones.HumanNeck);
            var upper  = P(Bones.HumanSpine3);
            var mid    = P(Bones.HumanSpine2);
            var lower  = P(Bones.HumanSpine1);
            var pelvis = P(Bones.HumanPelvis);
            var lCollar = P(Bones.HumanLCollarbone);
            var rCollar = P(Bones.HumanRCollarbone);
            var lElbow  = P(Bones.HumanLForearm2);
            var rElbow  = P(Bones.HumanRForearm2);
            var lHand   = P(Bones.HumanLPalm);
            var rHand   = P(Bones.HumanRPalm);
            var lKnee   = P(Bones.HumanLThigh2);
            var rKnee   = P(Bones.HumanRThigh2);
            var lFoot   = P(Bones.HumanLFoot);
            var rFoot   = P(Bones.HumanRFoot);

            Vector2? anchor = mid ?? upper ?? lower ?? pelvis ?? neck ?? head;
            if (!anchor.HasValue) { HasScreenData = false; return false; }
            var a = anchor.Value;

            int idx2 = 0;
            ScreenBuffer[idx2++] = head   ?? a; ScreenBuffer[idx2++] = neck  ?? a;
            ScreenBuffer[idx2++] = neck   ?? a; ScreenBuffer[idx2++] = upper ?? a;
            ScreenBuffer[idx2++] = upper  ?? a; ScreenBuffer[idx2++] = a;
            ScreenBuffer[idx2++] = a;           ScreenBuffer[idx2++] = lower ?? a;
            ScreenBuffer[idx2++] = lower  ?? a; ScreenBuffer[idx2++] = pelvis ?? a;
            ScreenBuffer[idx2++] = pelvis ?? a; ScreenBuffer[idx2++] = lKnee ?? a;
            ScreenBuffer[idx2++] = lKnee  ?? a; ScreenBuffer[idx2++] = lFoot ?? a;
            ScreenBuffer[idx2++] = pelvis ?? a; ScreenBuffer[idx2++] = rKnee ?? a;
            ScreenBuffer[idx2++] = rKnee  ?? a; ScreenBuffer[idx2++] = rFoot ?? a;
            ScreenBuffer[idx2++] = lCollar ?? a; ScreenBuffer[idx2++] = lElbow ?? a;
            ScreenBuffer[idx2++] = lElbow  ?? a; ScreenBuffer[idx2++] = lHand  ?? a;
            ScreenBuffer[idx2++] = rCollar ?? a; ScreenBuffer[idx2++] = rElbow ?? a;
            ScreenBuffer[idx2++] = rElbow  ?? a; ScreenBuffer[idx2++] = rHand  ?? a;

            HasScreenData = true;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector2? ProjectBone(Bones bone, Vector2 contentMin, int widgetW, int widgetH)
        {
            if (!_boneIndex.TryGetValue(bone, out int idx)) return null;
            var entry = _bones[idx];
            if (!entry.HasPosition) return null;

            var worldPos = entry.WorldPosition;
            if (!CameraManager.WorldToScreen(ref worldPos, out var scrPos))
                return null;

            if (CameraManager.ViewportWidth <= 0 || CameraManager.ViewportHeight <= 0)
                return null;

            float nx = scrPos.X / CameraManager.ViewportWidth;
            float ny = scrPos.Y / CameraManager.ViewportHeight;
            return new Vector2(contentMin.X + nx * widgetW, contentMin.Y + ny * widgetH);
        }

            }
        }
