

using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    /// <summary>
    /// Contains 'High Alert' Feature Code.
    /// Used for Radar Aimlines and ESP Feature.
    /// </summary>
    public static class HighAlert
    {
        /// <summary>
        /// Checks if a source target is facing the destination target.
        /// Part of the High Alert Feature Module.
        /// </summary>
        /// <param name="source">Source Target.</param>
        /// <param name="target">Destination Target.</param>
        /// <param name="maxDist">(Optional) Max distance to perform this check on. If exceeded returns false.</param>
        /// <returns>True if source is facing the destination target, otherwise False.</returns>
        public static bool IsFacingTarget(this Player source, IPlayer target, float? maxDist = null)
        {
            var distance = Vector3.Distance(source.Position, target.Position);
            if (maxDist is float maxDistFloat && distance > maxDistFloat)
                return false;

            // Calculate the 3D vector from source to target (including vertical component)
            var directionToTarget = Vector3.Normalize(target.Position - source.Position);

            // Convert source rotation to a direction vector
            var sourceDirection = Vector3.Normalize(RotationToDirection(source.Rotation));

            // Calculate the angle between source direction and the direction to the target
            var dotProduct = Vector3.Dot(sourceDirection, directionToTarget);
            var angle = (float)Math.Acos(dotProduct); // Result in radians

            // Convert angle to degrees for easier interpretation (optional)
            var angleInDegrees = angle * (180f / (float)Math.PI);

            var angleThreshold =
                31.3573 - 3.51726 *
                Math.Log(Math.Abs(0.626957 - 15.6948 * distance)); // Max degrees variance based on distance variable
            if (angleThreshold < 1f)
                angleThreshold = 1f; // Non linear equation, handle low/negative results

            return angleInDegrees <= angleThreshold;
        }

        /// <summary>
        /// Draw High Alert warning around edge of user's screen.
        /// </summary>
        public static void DrawHighAlertESP(SKCanvas canvas, Player target)
        {
            var targetPos = target is BtrOperator btr ?
                btr.Position : target.Skeleton.Bones[Bones.HumanSpine2].Position;
            if (CameraManagerBase.WorldToScreen(ref targetPos, out var targetScrPos, true))
            {
                canvas.DrawLine(targetScrPos, new SKPoint(CameraManagerBase.Viewport.Width / 2, CameraManagerBase.Viewport.Height),
                    SKPaints.PaintHighAlertAimlineESP);
            }
            else
            {
                var screenBorder = new SKRect(CameraManagerBase.Viewport.Left, CameraManagerBase.Viewport.Top, CameraManagerBase.Viewport.Right, CameraManagerBase.Viewport.Bottom);
                canvas.DrawRect(screenBorder, SKPaints.PaintHighAlertBorderESP);
            }
        }

        public static Vector3 RotationToDirection(Vector2 rotation)
        {
            // Convert rotation (yaw, pitch) to a direction vector
            // This might need adjustments based on how you define rotation
            var yaw = (float)rotation.X.ToRadians();
            var pitch = (float)rotation.Y.ToRadians();
            Vector3 direction;
            direction.X = (float)(Math.Cos(pitch) * Math.Sin(yaw));
            direction.Y = (float)Math.Sin(-pitch); // Negative pitch because in Unity, as pitch increases, we look down
            direction.Z = (float)(Math.Cos(pitch) * Math.Cos(yaw));

            return Vector3.Normalize(direction);
        }
    }
}