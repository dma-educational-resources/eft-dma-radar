using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Misc;
using SkiaSharp;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Common.Unity
{
    public abstract class CameraManagerBase
    {
        /// <summary>
        /// FPS Camera (unscoped).
        /// </summary>
        public virtual ulong FPSCamera => throw new NotImplementedException(nameof(FPSCamera));
        /// <summary>
        /// Optic Camera (ads/scoped).
        /// </summary>
        public virtual ulong OpticCamera => throw new NotImplementedException(nameof(OpticCamera));
        /// <summary>
        /// True if Optic Camera is currently active.
        /// Uses DerefIsAddedOffset because cameras are dereferenced by +0x10.
        /// </summary>
        protected bool OpticCameraActive => Memory.ReadValue<bool>(OpticCamera + UnityOffsets.Camera.DerefIsAddedOffset, false);
        public bool IsOpticCameraActive => OpticCameraActive;

        protected CameraManagerBase()
        {
        }


        #region Static Interfaces

        private const int VIEWPORT_TOLERANCE = 800;
        private static readonly Lock _viewportSync = new();

        /// <summary>
        /// True if ESP is currently rendering.
        /// </summary>
        public static bool EspRunning { get; set; }
        /// <summary>
        /// Game Viewport (Monitor Coordinates).
        /// </summary>
        public static Rectangle Viewport { get; private set; }
        /// <summary>
        /// Center of Game Viewport.
        /// </summary>
        public static SKPoint ViewportCenter => new SKPoint(Viewport.Width / 2f, Viewport.Height / 2f);
        /// <summary>
        /// True if LocalPlayer's Optic Camera is active (scope).
        /// </summary>
        public static bool IsScoped { get; protected set; }
        /// <summary>
        /// True if LocalPlayer is Aiming Down Sights (any sight/scope/irons).
        /// </summary>
        public static bool IsADS { get; protected set; }

        protected static float _fov;
        protected static float _aspect;
        protected static readonly ViewMatrix _viewMatrix = new();

        /// <summary>
        /// Update the Viewport Dimensions for Camera Calculations.
        /// Uses the actual resolution of the selected ESP monitor.
        /// </summary>
        public static void UpdateViewportRes()
        {
            lock (_viewportSync)
            {
                int width = SharedProgram.Config.MonitorWidth;
                int height = SharedProgram.Config.MonitorHeight;

                // Try to get actual monitor resolution from selected ESP target monitor
                try
                {
                    // Cast to actual Config type to access ESP property
                    if (SharedProgram.Config is eft_dma_radar.UI.Misc.Config cfg)
                    {
                        var monitor = eft_dma_radar.UI.Misc.MonitorInfo.GetMonitor(cfg.ESP.EspTargetScreen);
                        if (monitor != null)
                        {
                            width = monitor.Width;
                            height = monitor.Height;
                            Log.WriteLine($"[CameraManager] Viewport set to Monitor {monitor.Index}: {width}x{height}");
                        }
                        else
                        {
                            Log.WriteLine($"[CameraManager] Monitor not found, using config: {width}x{height}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[CameraManager] Error getting monitor info: {ex.Message}, using {width}x{height}");
                }

                Viewport = new Rectangle(0, 0, width, height);
            }
        }

        /// <summary>
        /// Translates 3D World Positions to 2D Screen Positions.
        /// </summary>
        /// <param name="worldPos">Entity's world position.</param>
        /// <param name="scrPos">Entity's screen position.</param>
        /// <param name="onScreenCheck">Check if the screen positions are 'on screen'. Returns false if off screen.</param>
        /// <returns>True if successful, otherwise False.</returns>
        public static bool WorldToScreen(ref Vector3 worldPos, out SKPoint scrPos, bool onScreenCheck = false, bool useTolerance = false)
        {
            // Reject invalid positions at or near world origin (0,0,0)
            // IL2CPP: Some bones may fail to read and stay at origin
            if (worldPos.LengthSquared() < 1f)
            {
                scrPos = default;
                return false;
            }

            float w = Vector3.Dot(_viewMatrix.Translation, worldPos) + _viewMatrix.M44; // Transposed

            if (w < 0.098f)
            {
                scrPos = default;
                return false;
            }

            float x = Vector3.Dot(_viewMatrix.Right, worldPos) + _viewMatrix.M14; // Transposed
            float y = Vector3.Dot(_viewMatrix.Up, worldPos) + _viewMatrix.M24; // Transposed

            if (IsScoped)
            {
                float angleRadHalf = (MathF.PI / 180f) * _fov * 0.5f;
                float angleCtg = MathF.Cos(angleRadHalf) / MathF.Sin(angleRadHalf);

                x /= angleCtg * _aspect * 0.5f;
                y /= angleCtg * 0.5f;
            }

            var center = ViewportCenter;
            scrPos = new()
            {
                X = center.X * (1f + x / w),
                Y = center.Y * (1f - y / w)
            };

            if (onScreenCheck)
            {
                int left = useTolerance ? Viewport.Left - VIEWPORT_TOLERANCE : Viewport.Left;
                int right = useTolerance ? Viewport.Right + VIEWPORT_TOLERANCE : Viewport.Right;
                int top = useTolerance ? Viewport.Top - VIEWPORT_TOLERANCE : Viewport.Top;
                int bottom = useTolerance ? Viewport.Bottom + VIEWPORT_TOLERANCE : Viewport.Bottom;
                // Check if the screen position is within the screen boundaries
                if (scrPos.X < left || scrPos.X > right ||
                    scrPos.Y < top || scrPos.Y > bottom)
                {
                    scrPos = default;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the FOV Magnitude (Length) between a point, and the center of the screen.
        /// </summary>
        /// <param name="point">Screen point to calculate FOV Magnitude of.</param>
        /// <returns>Screen distance from the middle of the screen (FOV Magnitude).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFovMagnitude(SKPoint point)
        {
            return Vector2.Distance(ViewportCenter.AsVector2(), point.AsVector2());
        }

        /// <summary>
        /// Builds a synthetic <see cref="ViewMatrix"/> from a world-space position and EFT rotation angles,
        /// using the same transposed convention as the live game view matrix read from memory.
        /// Suitable for projecting bones seen from a player other than local.
        /// </summary>
        /// <param name="position">World-space camera origin (player eye position).</param>
        /// <param name="yawDeg">Horizontal rotation in degrees (EFT Rotation.X).</param>
        /// <param name="pitchDeg">Vertical rotation in degrees (EFT Rotation.Y, positive = down).</param>
        public static ViewMatrix BuildViewMatrix(Vector3 position, float yawDeg, float pitchDeg)
        {
            // EFT angles: yaw rotates around Y axis, pitch rotates around X axis.
            // Positive pitch = looking down in EFT convention.
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = -pitchDeg * (MathF.PI / 180f); // negate: EFT positive = down, we want positive = up

            float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
            float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);

            // Camera basis in world space:
            //   forward = (sin(yaw)*cos(pitch),  sin(pitch), cos(yaw)*cos(pitch))
            //   right   = (cos(yaw),             0,          -sin(yaw))
            //   up      = (-sin(yaw)*sin(pitch), cos(pitch), -cos(yaw)*sin(pitch))
            var forward = new Vector3(sy * cp, sp, cy * cp);
            var right = new Vector3(cy, 0f, -sy);
            var up = new Vector3(-sy * sp, cp, -cy * sp);

            // View matrix rows (transposed VP) — matching WorldToScreen convention:
            //   Translation row  = forward  (used for w depth)
            //   Right row        = right    (used for x)
            //   Up row           = up       (used for y)
            //   M44 = -(forward · position)
            //   M14 = -(right   · position)
            //   M24 = -(up      · position)
            var vm = new ViewMatrix
            {
                Translation = forward,
                Right = right,
                Up = up,
                M44 = -Vector3.Dot(forward, position),
                M14 = -Vector3.Dot(right, position),
                M24 = -Vector3.Dot(up, position),
            };

            return vm;
        }
        #endregion
    }
}