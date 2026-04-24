namespace eft_dma_radar.Arena.GameWorld
{
    /// <summary>
    /// Player type classification (mirrors Silk's PlayerType).
    /// </summary>
    internal enum PlayerType
    {
        Default = 0,
        LocalPlayer,
        USEC,
        BEAR,
        PScav,
        AIScav,
        AIRaider,
        AIBoss,
        AIGuard,
    }

    /// <summary>
    /// Represents a single player tracked in the current Arena match.
    /// Written by the registration worker (identity) and realtime worker (position/rotation).
    /// </summary>
    internal sealed class Player
    {
        // ── Identity (written once during discovery) ──────────────────────

        /// <summary>Raw memory address of the player object (ObservedPlayerView or ClientPlayer).</summary>
        public ulong Base;

        /// <summary>Display name (nickname or role label for AI).</summary>
        public string Name = string.Empty;

        /// <summary>Account ID — not sent by Arena's server to other clients; always null.</summary>
        public string? AccountId;

        /// <summary>Profile ID string — may be set for AI too.</summary>
        public string? ProfileId;

        /// <summary>Player classification.</summary>
        public PlayerType Type;

        /// <summary>True if this is the local (MainPlayer) instance.</summary>
        public bool IsLocalPlayer;

        /// <summary>True if this player is AI-controlled.</summary>
        public bool IsAI;

        // ── State (updated each registration tick) ────────────────────────

        public bool IsActive;
        public bool IsAlive;

        /// <summary>True when the position has been successfully computed at least once.</summary>
        public bool HasValidPosition;

        // ── Realtime data (written by realtime scatter thread) ────────────

        /// <summary>World position in Unity space.</summary>
        public Vector3 Position;

        /// <summary>Yaw angle in degrees [0, 360).</summary>
        public float RotationYaw;

        /// <summary>Pitch angle in degrees.</summary>
        public float RotationPitch;

        // ── Transform cache (managed by RegisteredPlayers) ────────────────

        internal ulong TransformInternal;
        internal ulong VerticesAddr;
        internal int TransformIndex;
        internal volatile bool TransformReady;
        internal int[]? CachedIndices;

        internal ulong RotationAddr;
        internal volatile bool RotationReady;

        internal int ConsecutiveErrors;
        internal bool RealtimeEstablished;

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Name).Append(" (").Append(Type).Append(')');
            sb.Append(" @ ").Append(Position);
            sb.Append(" yaw=").Append(RotationYaw.ToString("F1")).Append('°');
            // AccountId omitted — Arena server never sends it to other clients
            if (ProfileId is not null)
                sb.Append(" prof=").Append(ProfileId);
            return sb.ToString();
        }
    }
}
