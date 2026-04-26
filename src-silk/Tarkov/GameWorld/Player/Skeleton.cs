using System.Buffers;
using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;

using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Per-player skeleton — reads 16 humanoid bone world positions via DMA scatter.
    /// <para>
    /// Each bone has its own TransformInternal + hierarchy/vertices/indices cache, just like
    /// the player's root position transform. Bone positions are updated on a dedicated camera
    /// worker thread (NOT the realtime worker) so skeleton reads never interfere with the
    /// primary position + rotation scatter loop.
    /// </para>
    /// <para>
    /// The aimview widget consumes <see cref="ScreenBuffer"/> (26 points = 13 line segments × 2 endpoints)
    /// which is populated by <see cref="UpdateScreenBuffer"/> using CameraManager.WorldToScreen().
    /// </para>
    /// </summary>
    internal sealed class Skeleton
    {
        /// <summary>Number of line-segment endpoints in the screen buffer (13 segments × 2).</summary>
        public const int JOINTS_COUNT = 26;

        public bool HasError = false;

        /// <summary>
        /// All 16 skeleton bones used for drawing. Order matches WPF's SkeletonBones enum.
        /// </summary>
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

        /// <summary>Cached bone data — one entry per bone.</summary>
        private readonly BoneEntry[] _bones;

        /// <summary>Bone enum → array index lookup (avoids O(N) scan per bone).</summary>
        private readonly Dictionary<Bones, int> _boneIndex;

        /// <summary>
        /// Screen-space buffer: 26 points forming 13 line segments (head→neck, neck→upper, etc.).
        /// Written by <see cref="UpdateScreenBuffer"/>, read by the aimview renderer.
        /// </summary>
        public readonly Vector2[] ScreenBuffer = new Vector2[JOINTS_COUNT];

        /// <summary>Whether the screen buffer contains valid data for the current frame.</summary>
        public volatile bool HasScreenData;

        /// <summary>Whether at least one bone has a valid world position.</summary>
        public volatile bool IsInitialized;

        /// <summary>Whether all bone transforms have been resolved (pointer chains walked).</summary>
        public volatile bool TransformsReady;

        /// <summary>
        /// True when the skeleton hierarchy is producing bone positions that are clearly
        /// detached from the player's body (e.g. stale TransformInternal after a respawn /
        /// teleport). Set with hysteresis (<see cref="BadTickThreshold"/> consecutive bad
        /// ticks) to avoid one-off false positives forcing a transform invalidation.
        /// Self-clears after a single clean tick.
        /// </summary>

        /// <summary>Consecutive ticks where any bone's world position diverged from the player.</summary>
        private int _consecutiveBadTicks;

        /// <summary>Number of consecutive bad ticks before <see cref="HasError"/> is raised.</summary>
        private const int BadTickThreshold = 3;

        /// <summary>Max distance (meters) from the player's anchor before a bone is considered detached.</summary>
        private const float MaxBoneDistance = 15f;

        /// <summary>Per-bone cached transform data.</summary>
        private sealed class BoneEntry
        {
            public readonly Bones Bone;
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public int TransformIndex;
            public int[]? CachedIndices;
            public bool Ready;

            /// <summary>Last successfully computed world position for this bone.</summary>
            public Vector3 WorldPosition;

            /// <summary>Whether <see cref="WorldPosition"/> contains a valid value.</summary>
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
        /// Attempts to create a Skeleton for the given player by walking the bone pointer chain.
        /// Returns null if the chain is unreadable (player data not yet initialized).
        /// <para>
        /// Chain: _playerBody → PlayerBody.SkeletonRootJoint → DizSkinningSkeleton._values →
        /// List._items → element[boneIndex] → +0x10 → TransformInternal.
        /// </para>
        /// </summary>
        internal static Skeleton? TryCreate(ulong playerBase, bool isObserved)
        {
            try
            {
                // Resolve the skeleton values list pointer
                ulong playerBodyOffset = isObserved
                    ? SDK.Offsets.ObservedPlayerView.PlayerBody
                    : SDK.Offsets.Player._playerBody;

                var playerBody = Memory.ReadPtr(playerBase + playerBodyOffset, false);
                var skeletonRoot = Memory.ReadPtr(playerBody + SDK.Offsets.PlayerBody.SkeletonRootJoint, false);
                var values = Memory.ReadPtr(skeletonRoot + SDK.Offsets.DizSkinningSkeleton._values, false);
                var itemsArr = Memory.ReadPtr(values + List.ArrOffset, false);

                if (!itemsArr.IsValidVirtualAddress())
                    return null;

                var entries = new BoneEntry[_allBones.Length];

                for (int i = 0; i < _allBones.Length; i++)
                {
                    var bone = _allBones[i];
                    entries[i] = new BoneEntry(bone);

                    try
                    {
                        // List element: itemsArr + 0x20 + boneIndex * 8 → Transform component → +0x10 → TransformInternal
                        ulong elemAddr = itemsArr + List.ArrStartOffset + (uint)bone * 0x8;
                        var transformComponent = Memory.ReadPtr(elemAddr, false);
                        var transformInternal = Memory.ReadPtr(transformComponent + 0x10, false);

                        if (!transformInternal.IsValidVirtualAddress())
                            continue;

                        var taIndex = Memory.ReadValue<int>(transformInternal + TransformAccess.IndexOffset, false);
                        var taHierarchy = Memory.ReadPtr(transformInternal + TransformAccess.HierarchyOffset, false);

                        if (taIndex < 0 || taIndex > 128_000 || !taHierarchy.IsValidVirtualAddress())
                            continue;

                        var verticesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.VerticesOffset, false);
                        var indicesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.IndicesOffset, false);

                        if (!verticesAddr.IsValidVirtualAddress() || !indicesAddr.IsValidVirtualAddress())
                            continue;

                        int count = taIndex + 1;
                        var indices = Memory.ReadArray<int>(indicesAddr, count, false);

                        entries[i].TransformInternal = transformInternal;
                        entries[i].TransformIndex = taIndex;
                        entries[i].VerticesAddr = verticesAddr;
                        entries[i].CachedIndices = indices;
                        entries[i].Ready = true;
                    }
                    catch
                    {
                        // Individual bone failure — leave entry as not-ready
                    }
                }

                // Need at least the anchor bone (Spine2 = MidTorso) to be useful
                int readyCount = 0;
                bool hasAnchor = false;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Ready)
                    {
                        readyCount++;
                        if (entries[i].Bone == Bones.HumanSpine2)
                            hasAnchor = true;
                    }
                }

                if (!hasAnchor || readyCount < 4)
                {
                    Log.Write(AppLogLevel.Debug,
                        $"[Skeleton] TryCreate failed — anchor={hasAnchor}, ready={readyCount}/{entries.Length}");
                    return null;
                }

                var skeleton = new Skeleton(entries)
                {
                    TransformsReady = true,
                    IsInitialized = true
                };

                Log.Write(AppLogLevel.Debug,
                    $"[Skeleton] Created — {readyCount}/{entries.Length} bones ready");
                return skeleton;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[Skeleton] TryCreate failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Updates bone world positions for multiple skeletons in a single batched DMA scatter.
        /// This avoids N separate scatter open/execute/close cycles (one per skeleton).
        /// Called from the camera worker thread.
        /// </summary>
        internal static void UpdateBonePositionsBatched(ReadOnlySpan<RegisteredPlayers.PlayerEntry> players)
        {
            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            // Prepare phase: enqueue all bone vertex reads across all skeletons
            for (int s = 0; s < players.Length; s++)
            {
                var skeleton = players[s].Skeleton;
                if (skeleton is null)
                    continue;

                var bones = skeleton._bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    var entry = bones[i];
                    if (!entry.Ready)
                        continue;

                    scatter.PrepareReadArray<TrsX>(entry.VerticesAddr, entry.TransformIndex + 1);
                }
            }

            scatter.Execute();

            // Process phase: compute world positions from the single scatter result
            for (int s = 0; s < players.Length; s++)
            {
                var playerEntry = players[s];
                var skeleton = playerEntry.Skeleton;
                if (skeleton is null)
                    continue;

                var bones = skeleton._bones;
                int badBones = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    var entry = bones[i];
                    if (!entry.Ready)
                        continue;

                    int vcount = entry.TransformIndex + 1;
                    var rented = ArrayPool<TrsX>.Shared.Rent(vcount);
                    try
                    {
                        var vertices = rented.AsSpan(0, vcount);
                        if (!scatter.ReadSpan<TrsX>(entry.VerticesAddr, vertices))
                            continue;

                        try
                        {
                            var worldPos = TrsX.ComputeWorldPosition(vertices, entry.CachedIndices!, entry.TransformIndex);
                            if (float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z))
                            {
                                entry.WorldPosition = worldPos;
                                entry.HasPosition = true;
                            }
                        }
                        catch
                        {
                            // Transient failure — keep last valid position
                        }

                        // Sanity check: any limb >MaxBoneDistance from the player's root is
                        // a strong indicator the cached transform hierarchy is stale (e.g.
                        // post-teleport or post-respawn). We require >=2 bad bones AND
                        // multiple consecutive bad ticks before flipping HasError so a
                        // single cross-thread freshness mismatch (Player.Position lagging
                        // the skeleton scatter by a tick) doesn't drop the marker.
                        if (entry.HasPosition &&
                            Vector3.Distance(entry.WorldPosition, playerEntry.Player.Position) > MaxBoneDistance)
                        {
                            badBones++;
                        }
                    }
                    finally
                    {
                        ArrayPool<TrsX>.Shared.Return(rented);
                    }
                }

                if (badBones >= 2)
                {
                    if (++skeleton._consecutiveBadTicks >= BadTickThreshold)
                        skeleton.HasError = true;
                }
                else
                {
                    skeleton._consecutiveBadTicks = 0;
                    skeleton.HasError = false;
                }
            }
        }

        /// <summary>
        /// Projects all bone world positions through CameraManager.WorldToScreen() and fills
        /// <see cref="ScreenBuffer"/> with 26 points (13 line segments).
        /// Returns true if the anchor bone (mid-torso) projected successfully.
        /// </summary>
        internal bool UpdateScreenBuffer(Vector2 contentMin, int widgetW, int widgetH)
        {
            // Resolve all bones to screen space
            var head = ProjectBone(Bones.HumanHead, contentMin, widgetW, widgetH);
            var neck = ProjectBone(Bones.HumanNeck, contentMin, widgetW, widgetH);
            var upper = ProjectBone(Bones.HumanSpine3, contentMin, widgetW, widgetH);
            var mid = ProjectBone(Bones.HumanSpine2, contentMin, widgetW, widgetH);
            var lower = ProjectBone(Bones.HumanSpine1, contentMin, widgetW, widgetH);
            var pelvis = ProjectBone(Bones.HumanPelvis, contentMin, widgetW, widgetH);

            var lCollar = ProjectBone(Bones.HumanLCollarbone, contentMin, widgetW, widgetH);
            var rCollar = ProjectBone(Bones.HumanRCollarbone, contentMin, widgetW, widgetH);
            var lElbow = ProjectBone(Bones.HumanLForearm2, contentMin, widgetW, widgetH);
            var rElbow = ProjectBone(Bones.HumanRForearm2, contentMin, widgetW, widgetH);
            var lHand = ProjectBone(Bones.HumanLPalm, contentMin, widgetW, widgetH);
            var rHand = ProjectBone(Bones.HumanRPalm, contentMin, widgetW, widgetH);

            var lKnee = ProjectBone(Bones.HumanLThigh2, contentMin, widgetW, widgetH);
            var rKnee = ProjectBone(Bones.HumanRThigh2, contentMin, widgetW, widgetH);
            var lFoot = ProjectBone(Bones.HumanLFoot, contentMin, widgetW, widgetH);
            var rFoot = ProjectBone(Bones.HumanRFoot, contentMin, widgetW, widgetH);

            // Pick the first available torso-ish anchor. Previously this required
            // HumanSpine2 (mid) specifically — if it was clipped behind the camera
            // or its position hadn't been resolved yet, the whole skeleton was
            // suppressed and the widget fell back to a dot. Any of the torso bones
            // (or head/neck as a last resort) is good enough to anchor the fallback
            // segments for missing joints.
            Vector2? anchor = mid ?? upper ?? lower ?? pelvis ?? neck ?? head;
            if (!anchor.HasValue)
            {
                HasScreenData = false;
                return false;
            }

            // Fallback: use anchor for any bone that failed projection
            var midV = anchor.Value;

            int idx = 0;
            // Head → neck → upper → mid → lower → pelvis (spine)
            ScreenBuffer[idx++] = head ?? midV;
            ScreenBuffer[idx++] = neck ?? midV;
            ScreenBuffer[idx++] = neck ?? midV;
            ScreenBuffer[idx++] = upper ?? midV;
            ScreenBuffer[idx++] = upper ?? midV;
            ScreenBuffer[idx++] = midV;
            ScreenBuffer[idx++] = midV;
            ScreenBuffer[idx++] = lower ?? midV;
            ScreenBuffer[idx++] = lower ?? midV;
            ScreenBuffer[idx++] = pelvis ?? midV;

            // Pelvis → left knee → left foot
            ScreenBuffer[idx++] = pelvis ?? midV;
            ScreenBuffer[idx++] = lKnee ?? midV;
            ScreenBuffer[idx++] = lKnee ?? midV;
            ScreenBuffer[idx++] = lFoot ?? midV;

            // Pelvis → right knee → right foot
            ScreenBuffer[idx++] = pelvis ?? midV;
            ScreenBuffer[idx++] = rKnee ?? midV;
            ScreenBuffer[idx++] = rKnee ?? midV;
            ScreenBuffer[idx++] = rFoot ?? midV;

            // Left collar → left elbow → left hand
            ScreenBuffer[idx++] = lCollar ?? midV;
            ScreenBuffer[idx++] = lElbow ?? midV;
            ScreenBuffer[idx++] = lElbow ?? midV;
            ScreenBuffer[idx++] = lHand ?? midV;

            // Right collar → right elbow → right hand
            ScreenBuffer[idx++] = rCollar ?? midV;
            ScreenBuffer[idx++] = rElbow ?? midV;
            ScreenBuffer[idx++] = rElbow ?? midV;
            ScreenBuffer[idx++] = rHand ?? midV;

            HasScreenData = true;
            return true;
        }

        /// <summary>
        /// Gets the world position of a specific bone, or null if not available.
        /// </summary>
        internal Vector3? GetBonePosition(Bones bone)
        {
            if (_boneIndex.TryGetValue(bone, out int idx))
            {
                var entry = _bones[idx];
                if (entry.HasPosition)
                    return entry.WorldPosition;
            }
            return null;
        }

        /// <summary>
        /// Projects a single bone through CameraManager W2S, remapping to widget coordinates.
        /// Returns null if the bone has no valid position or projection fails.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector2? ProjectBone(Bones bone, Vector2 contentMin, int widgetW, int widgetH)
        {
            if (!_boneIndex.TryGetValue(bone, out int idx))
                return null;

            var entry = _bones[idx];
            if (!entry.HasPosition)
                return null;

            var worldPos = entry.WorldPosition;
            if (!CameraManager.WorldToScreen(ref worldPos, out var scrPos))
                return null;

            // Remap: game viewport → widget bounds
            float nx = scrPos.X / CameraManager.ViewportWidth;
            float ny = scrPos.Y / CameraManager.ViewportHeight;
            return new Vector2(contentMin.X + nx * widgetW, contentMin.Y + ny * widgetH);
        }

            }
        }
