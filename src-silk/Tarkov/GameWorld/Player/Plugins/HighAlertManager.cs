namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player.Plugins
{
    /// <summary>
    /// High Alert detection — decides whether a hostile player is currently aiming at the local player
    /// using a distance-adaptive angle threshold. Mirrors the WPF HighAlert plugin's <c>IsFacingTarget</c>
    /// formula (logarithmic tightening with distance, min 1° cone).
    /// </summary>
    internal static class HighAlertManager
    {
        /// <summary>
        /// Duration (ms) the <see cref="Player.IsFacingLocalPlayer"/> flag stays true after the last
        /// positive facing check — smooths out flicker when an enemy briefly looks away.
        /// </summary>
        private const long AlertHoldMs = 500;

        /// <summary>Maximum distance (meters) to bother running the check at all.</summary>
        private const float MaxCheckDistance = 300f;

        /// <summary>Squared variant — used for cheap early-reject before the sqrt in Vector3.Distance.</summary>
        private const float MaxCheckDistanceSqr = MaxCheckDistance * MaxCheckDistance;

        /// <summary>
        /// Evaluate every hostile human in <paramref name="players"/> against the local player.
        /// Updates each player's <c>IsFacingLocalPlayer</c> / <c>LastFacedLocalTick</c>.
        /// Cheap — pure math, no DMA.
        /// </summary>
        public static void Tick(LocalPlayer localPlayer, IEnumerable<Player> players)
        {
            if (localPlayer is null)
                return;

            long now = Environment.TickCount64;
            Vector3 localPos = localPlayer.Position;

            foreach (var p in players)
            {
                if (p is null || p.IsLocalPlayer || !p.IsAlive || !p.HasValidPosition)
                    continue;
                if (!p.IsHostile)
                {
                    p.IsFacingLocalPlayer = false;
                    continue;
                }

                bool facing = IsFacingLocalPlayer(p, localPos);
                if (facing)
                {
                    p.IsFacingLocalPlayer = true;
                    p.LastFacedLocalTick = now;
                }
                else if (p.LastFacedLocalTick == 0 || now - p.LastFacedLocalTick > AlertHoldMs)
                {
                    p.IsFacingLocalPlayer = false;
                }
            }
        }

        /// <summary>
        /// Returns true if <paramref name="source"/>'s look direction is aimed at <paramref name="localPos"/>
        /// within a distance-adaptive cone (WPF formula: <c>31.3573 - 3.51726 * ln(|0.626957 - 15.6948 * d|)</c>).
        /// </summary>
        public static bool IsFacingLocalPlayer(Player source, Vector3 localPos)
        {
            var delta = localPos - source.Position;
            float distSqr = delta.LengthSquared();
            // Cheap early-reject without a sqrt — skips out-of-range players every realtime tick.
            if (distSqr <= 0.0001f || distSqr > MaxCheckDistanceSqr)
                return false;

            float dist = MathF.Sqrt(distSqr);
            var toTarget = delta / dist;
            var look = YawPitchToDirection(source.RotationYaw, source.RotationPitch);

            float dot = Vector3.Dot(look, toTarget);
            if (dot < -1f) dot = -1f;
            else if (dot > 1f) dot = 1f;

            float angleDeg = MathF.Acos(dot) * (180f / MathF.PI);

            double threshold = 31.3573 - 3.51726 * Math.Log(Math.Abs(0.626957 - 15.6948 * dist));
            if (threshold < 1.0) threshold = 1.0;

            return angleDeg <= (float)threshold;
        }

        /// <summary>
        /// Converts EFT yaw/pitch (degrees) to a unit look direction in world space.
        /// Negative pitch is down in Unity.
        /// </summary>
        public static Vector3 YawPitchToDirection(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = pitchDeg * (MathF.PI / 180f);
            Vector3 d;
            d.X = MathF.Cos(pitch) * MathF.Sin(yaw);
            d.Y = MathF.Sin(-pitch);
            d.Z = MathF.Cos(pitch) * MathF.Cos(yaw);
            return Vector3.Normalize(d);
        }
    }
}
