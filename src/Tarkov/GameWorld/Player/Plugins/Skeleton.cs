using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.UI.ESP;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    /// <summary>
    /// Contains abstractions for drawing Player Skeletons.
    /// </summary>
    public sealed class Skeleton
    {
        private const int JOINTS_COUNT = 26;
        private const float MAX_SCREEN_COORD = 10000f;

        /// <summary>
        /// Bones Buffer for full ESP (world overlay).
        /// </summary>
        public static readonly SKPoint[] ESPBuffer = new SKPoint[JOINTS_COUNT];

        /// <summary>
        /// Per-skeleton buffer for Aimview widget (NOT static).
        /// </summary>
        private readonly SKPoint[] _espWidgetBuffer = new SKPoint[JOINTS_COUNT];
        public ReadOnlySpan<SKPoint> ESPWidgetBuffer => _espWidgetBuffer;

        /// <summary>
        /// All Skeleton Bones.
        /// </summary>
        public static ReadOnlyMemory<Bones> AllSkeletonBones { get; }
            = Enum.GetValues<SkeletonBones>().Cast<Bones>().ToArray();

        public static ReadOnlyMemory<Bones> AllTorsoBones { get; }
            = Enum.GetValues<TorsoBones>().Cast<Bones>().ToArray();

        public static ReadOnlyMemory<Bones> AllArmsBones { get; }
            = Enum.GetValues<ArmsBones>().Cast<Bones>().ToArray();

        public static ReadOnlyMemory<Bones> AllLegsBones { get; }
            = Enum.GetValues<LegsBones>().Cast<Bones>().ToArray();

        private readonly Dictionary<Bones, UnityTransform> _bones;
        private readonly IPlayer _player;
        private Vector3 _lastRootPos;
        private int _staticRootTicks;
        private const int STUCK_TICK_THRESHOLD = 20;
        private const float ROOT_EPSILON = 0.15f;

        public bool IsLikelyStuck { get; set; }
        public UnityTransform Root { get; private set; }
        public IReadOnlyDictionary<Bones, UnityTransform> Bones => _bones;
        public int VerticesCount { get; private set; }

        public Skeleton(IPlayer player, Func<Bones, uint[]> getTransformChainFunc)
        {
            _player = player;

            var tiRoot = Memory.ReadPtrChain(player.Base, getTransformChainFunc(eft_dma_radar.Common.Unity.Bones.HumanBase));
            Root = new UnityTransform(tiRoot);

            var bones = new Dictionary<Bones, UnityTransform>(AllSkeletonBones.Length + 1)
            {
                [eft_dma_radar.Common.Unity.Bones.HumanBase] = Root
            };

            foreach (var bone in AllSkeletonBones.Span)
            {
                var tiBone = Memory.ReadPtrChain(player.Base, getTransformChainFunc(bone));
                bones[bone] = new UnityTransform(tiBone);
            }

            _bones = bones;

            VerticesCount = _bones.Values.Max(t => t.Count);
            VerticesCount = Math.Max(VerticesCount, Root.Count);
        }
        public void UpdateStuckDetection()
        {
            if (!Root.HasValidPosition)
            {
                _staticRootTicks = 0;
                IsLikelyStuck = false;
                return;
            }

            var current = Root.Position;

            if (Vector3.DistanceSquared(current, _lastRootPos) <= ROOT_EPSILON * ROOT_EPSILON)
            {
                _staticRootTicks++;
            }
            else
            {
                _staticRootTicks = 0;
                _lastRootPos = current;
                IsLikelyStuck = false;
                return;
            }

            if (_staticRootTicks >= STUCK_TICK_THRESHOLD)
            {
                IsLikelyStuck = true;
            }
        }

        public bool HasValidPosition => Root?.HasValidPosition ?? false;

        #region Cached Screen Positions

        private SKPoint _lastMidTorso, _lastHead, _lastNeck, _lastLeftCollar, _lastRightCollar;
        private SKPoint _lastLeftHand, _lastRightHand, _lastUpperTorso, _lastLowerTorso, _lastPelvis;
        private SKPoint _lastLeftFoot, _lastRightFoot, _lastLeftKnee, _lastRightKnee;
        private SKPoint _lastLeftElbow, _lastRightElbow;
        private bool _hasValidCache;

        private static bool IsValid(SKPoint p)
        {
            return
                !float.IsNaN(p.X) &&
                !float.IsNaN(p.Y) &&
                Math.Abs(p.X) <= MAX_SCREEN_COORD &&
                Math.Abs(p.Y) <= MAX_SCREEN_COORD;
        }
        /// <summary>
        /// Updates the static ESP Buffer with the current Skeleton Bone Screen Coordinates.<br />
        /// See <see cref="Skeleton.ESPBuffer"/><br />
        /// Uses cached last-valid positions for bones that fail to convert (prevents flickering).
        /// NOT THREAD SAFE!
        /// </summary>
        /// <returns>True if successful, otherwise False.</returns>
        public bool UpdateESPBuffer()
        {
            // MidTorso is the anchor - if this fails and we have no cache, abort
            if (!CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanSpine2].Position, out var midTorsoScreen, true, true))
            {
                if (!_hasValidCache) return false;
                midTorsoScreen = _lastMidTorso;
            }
            else _lastMidTorso = midTorsoScreen;
            
            // For all other bones: use new position if valid, otherwise use cached
            var headScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanHead].Position, out var h) ? (_lastHead = h) : _lastHead;
            var neckScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanNeck].Position, out var n) ? (_lastNeck = n) : _lastNeck;
            var leftCollarScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanLCollarbone].Position, out var lc) ? (_lastLeftCollar = lc) : _lastLeftCollar;
            var rightCollarScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanRCollarbone].Position, out var rc) ? (_lastRightCollar = rc) : _lastRightCollar;
            var leftHandScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanLPalm].Position, out var lh) ? (_lastLeftHand = lh) : _lastLeftHand;
            var rightHandScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanRPalm].Position, out var rh) ? (_lastRightHand = rh) : _lastRightHand;
            var upperTorsoScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanSpine3].Position, out var ut) ? (_lastUpperTorso = ut) : _lastUpperTorso;
            var lowerTorsoScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanSpine1].Position, out var lt) ? (_lastLowerTorso = lt) : _lastLowerTorso;
            var pelvisScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanPelvis].Position, out var p) ? (_lastPelvis = p) : _lastPelvis;
            var leftFootScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanLFoot].Position, out var lf) ? (_lastLeftFoot = lf) : _lastLeftFoot;
            var rightFootScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanRFoot].Position, out var rf) ? (_lastRightFoot = rf) : _lastRightFoot;
            var leftKneeScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanLThigh2].Position, out var lk) ? (_lastLeftKnee = lk) : _lastLeftKnee;
            var rightKneeScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanRThigh2].Position, out var rk) ? (_lastRightKnee = rk) : _lastRightKnee;
            var leftElbowScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanLForearm2].Position, out var le) ? (_lastLeftElbow = le) : _lastLeftElbow;
            var rightElbowScreen = CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanRForearm2].Position, out var re) ? (_lastRightElbow = re) : _lastRightElbow;
            
            _hasValidCache = true;
            
            int index = 0;
            // Head to left foot
            ESPBuffer[index++] = headScreen;
            ESPBuffer[index++] = neckScreen;
            ESPBuffer[index++] = neckScreen;
            ESPBuffer[index++] = upperTorsoScreen;
            ESPBuffer[index++] = upperTorsoScreen;
            ESPBuffer[index++] = midTorsoScreen;
            ESPBuffer[index++] = midTorsoScreen;
            ESPBuffer[index++] = lowerTorsoScreen;
            ESPBuffer[index++] = lowerTorsoScreen;
            ESPBuffer[index++] = pelvisScreen;
            ESPBuffer[index++] = pelvisScreen;
            ESPBuffer[index++] = leftKneeScreen;
            ESPBuffer[index++] = leftKneeScreen;
            ESPBuffer[index++] = leftFootScreen; // 14
            // Pelvis to right foot
            ESPBuffer[index++] = pelvisScreen;
            ESPBuffer[index++] = rightKneeScreen;
            ESPBuffer[index++] = rightKneeScreen;
            ESPBuffer[index++] = rightFootScreen; // 18
            // Left collar to left hand
            ESPBuffer[index++] = leftCollarScreen;
            ESPBuffer[index++] = leftElbowScreen;
            ESPBuffer[index++] = leftElbowScreen;
            ESPBuffer[index++] = leftHandScreen; // 22
            // Right collar to right hand
            ESPBuffer[index++] = rightCollarScreen;
            ESPBuffer[index++] = rightElbowScreen;
            ESPBuffer[index++] = rightElbowScreen;
            ESPBuffer[index++] = rightHandScreen; // 26
            return true;
        }
        #endregion

        /// <summary>
        /// Updates the Aimview widget buffer (safe against corruption).
        /// </summary>
        public bool UpdateESPWidgetBuffer(float scaleX, float scaleY)
        {
            // Anchor bone
            if (!CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanSpine2].Position, out var mid))
            {
                if (!_hasValidCache || !IsValid(_lastMidTorso))
                    return false;

                mid = _lastMidTorso;
            }
            else _lastMidTorso = mid;

            // Resolve all bones safely
            SKPoint Resolve(Bones b, ref SKPoint cache)
            {
                if (CameraManagerBase.WorldToScreen(ref _bones[b].Position, out var s))
                {
                    cache = s;
                    return s;
                }
                return IsValid(cache) ? cache : mid;
            }

            var head = Resolve(eft_dma_radar.Common.Unity.Bones.HumanHead, ref _lastHead);
            var neck = Resolve(eft_dma_radar.Common.Unity.Bones.HumanNeck, ref _lastNeck);
            var upper = Resolve(eft_dma_radar.Common.Unity.Bones.HumanSpine3, ref _lastUpperTorso);
            var lower = Resolve(eft_dma_radar.Common.Unity.Bones.HumanSpine1, ref _lastLowerTorso);
            var pelvis = Resolve(eft_dma_radar.Common.Unity.Bones.HumanPelvis, ref _lastPelvis);

            var lCollar = Resolve(eft_dma_radar.Common.Unity.Bones.HumanLCollarbone, ref _lastLeftCollar);
            var rCollar = Resolve(eft_dma_radar.Common.Unity.Bones.HumanRCollarbone, ref _lastRightCollar);
            var lElbow = Resolve(eft_dma_radar.Common.Unity.Bones.HumanLForearm2, ref _lastLeftElbow);
            var rElbow = Resolve(eft_dma_radar.Common.Unity.Bones.HumanRForearm2, ref _lastRightElbow);
            var lHand = Resolve(eft_dma_radar.Common.Unity.Bones.HumanLPalm, ref _lastLeftHand);
            var rHand = Resolve(eft_dma_radar.Common.Unity.Bones.HumanRPalm, ref _lastRightHand);

            var lKnee = Resolve(eft_dma_radar.Common.Unity.Bones.HumanLThigh2, ref _lastLeftKnee);
            var rKnee = Resolve(eft_dma_radar.Common.Unity.Bones.HumanRThigh2, ref _lastRightKnee);
            var lFoot = Resolve(eft_dma_radar.Common.Unity.Bones.HumanLFoot, ref _lastLeftFoot);
            var rFoot = Resolve(eft_dma_radar.Common.Unity.Bones.HumanRFoot, ref _lastRightFoot);

            _hasValidCache = true;

            int i = 0;
            Write(head); Write(neck);
            Write(neck); Write(upper);
            Write(upper); Write(mid);
            Write(mid); Write(lower);
            Write(lower); Write(pelvis);

            Write(pelvis); Write(lKnee);
            Write(lKnee); Write(lFoot);

            Write(pelvis); Write(rKnee);
            Write(rKnee); Write(rFoot);

            Write(lCollar); Write(lElbow);
            Write(lElbow); Write(lHand);

            Write(rCollar); Write(rElbow);
            Write(rElbow); Write(rHand);

            return i == JOINTS_COUNT;

            void Write(SKPoint p)
            {
                _espWidgetBuffer[i].X = p.X * scaleX;
                _espWidgetBuffer[i].Y = p.Y * scaleY;
                i++;
            }
        }

        #region Bone Enums

        public enum SkeletonBones : uint
        {
            Head = eft_dma_radar.Common.Unity.Bones.HumanHead,
            Neck = eft_dma_radar.Common.Unity.Bones.HumanNeck,
            UpperTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine3,
            MidTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine2,
            LowerTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine1,
            LeftShoulder = eft_dma_radar.Common.Unity.Bones.HumanLCollarbone,
            RightShoulder = eft_dma_radar.Common.Unity.Bones.HumanRCollarbone,
            LeftElbow = eft_dma_radar.Common.Unity.Bones.HumanLForearm2,
            RightElbow = eft_dma_radar.Common.Unity.Bones.HumanRForearm2,
            LeftHand = eft_dma_radar.Common.Unity.Bones.HumanLPalm,
            RightHand = eft_dma_radar.Common.Unity.Bones.HumanRPalm,
            Pelvis = eft_dma_radar.Common.Unity.Bones.HumanPelvis,
            LeftKnee = eft_dma_radar.Common.Unity.Bones.HumanLThigh2,
            RightKnee = eft_dma_radar.Common.Unity.Bones.HumanRThigh2,
            LeftFoot = eft_dma_radar.Common.Unity.Bones.HumanLFoot,
            RightFoot = eft_dma_radar.Common.Unity.Bones.HumanRFoot
        }

        public enum TorsoBones : uint
        {
            Neck = eft_dma_radar.Common.Unity.Bones.HumanNeck,
            UpperTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine3,
            MidTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine2,
            LowerTorso = eft_dma_radar.Common.Unity.Bones.HumanSpine1
        }

        public enum ArmsBones : uint
        {
            LeftShoulder = eft_dma_radar.Common.Unity.Bones.HumanLCollarbone,
            RightShoulder = eft_dma_radar.Common.Unity.Bones.HumanRCollarbone,
            LeftElbow = eft_dma_radar.Common.Unity.Bones.HumanLForearm2,
            RightElbow = eft_dma_radar.Common.Unity.Bones.HumanRForearm2,
            LeftHand = eft_dma_radar.Common.Unity.Bones.HumanLPalm,
            RightHand = eft_dma_radar.Common.Unity.Bones.HumanRPalm
        }

        public enum LegsBones : uint
        {
            Pelvis = eft_dma_radar.Common.Unity.Bones.HumanPelvis,
            LeftKnee = eft_dma_radar.Common.Unity.Bones.HumanLThigh2,
            RightKnee = eft_dma_radar.Common.Unity.Bones.HumanRThigh2,
            LeftFoot = eft_dma_radar.Common.Unity.Bones.HumanLFoot,
            RightFoot = eft_dma_radar.Common.Unity.Bones.HumanRFoot
        }
        /// <summary>
        /// Return screen coordinates with W2S transformation applied for Box ESP.
        /// </summary>
        /// <param name="baseScreen">Screen Coords of Base Position.</param>
        /// <returns>Box ESP Screen Coordinates.</returns>
        public SKRect? GetESPBox(SKPoint baseScreen)
        {
            if (!CameraManagerBase.WorldToScreen(ref _bones[eft_dma_radar.Common.Unity.Bones.HumanHead].Position, out var topScreen, true, true))
                return null;

            float height = Math.Abs(topScreen.Y - baseScreen.Y);
            float width = height / 2.05f;
            //overlay->draw_box(foot.x - (width / 2), foot.y, head.x + width, head.y + height, 2.0f); //ESP BOX

            return new SKRect()
            {
                Top = topScreen.Y,
                Left = topScreen.X - width / 2,
                Bottom = baseScreen.Y,
                Right = topScreen.X + width / 2
            };
        }
        /// <summary>
        /// Reset the Transform for this player, preserving last valid position.
        /// </summary>
        /// <param name="bone"></param>
        public void ResetTransform(Bones bone)
        {
            var oldTransform = _bones[bone];
            var lastValidPosition = oldTransform.HasValidPosition ? oldTransform.Position : (Vector3?)null;
            
            var transform = new UnityTransform(_bones[bone].TransformInternal, lastValidPosition);
            _bones[bone] = transform;
            if (bone is eft_dma_radar.Common.Unity.Bones.HumanBase)
                Root = transform;
        }        
        /// <summary>
        /// Clears cached ESP screen positions and rebuilds transforms.
        /// Does NOT destroy identity or bones.
        /// </summary>
        public void ResetESPCacheAndTransforms()
        {
            // Reset cached screen positions
            _lastMidTorso     = default;
            _lastHead         = default;
            _lastNeck         = default;
            _lastLeftCollar   = default;
            _lastRightCollar  = default;
            _lastLeftHand     = default;
            _lastRightHand    = default;
            _lastUpperTorso   = default;
            _lastLowerTorso   = default;
            _lastPelvis       = default;
            _lastLeftFoot     = default;
            _lastRightFoot    = default;
            _lastLeftKnee     = default;
            _lastRightKnee    = default;
            _lastLeftElbow    = default;
            _lastRightElbow   = default;
        
            _hasValidCache = false;
        
            // Clear static ESP buffer (important!)
            Array.Clear(ESPBuffer, 0, ESPBuffer.Length);
        
            // Rebuild all transforms (preserve last valid positions)
            foreach (var bone in _bones.Keys.ToList())
            {
                ResetTransform(bone);
            }
        }        
        #endregion
    }
}
