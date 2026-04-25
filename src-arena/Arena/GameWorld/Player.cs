namespace eft_dma_radar.Arena.GameWorld
{
    using SDK;

    /// <summary>
    /// Player type classification (mirrors Silk's PlayerType).
    /// </summary>
    internal enum PlayerType
    {
        Default = 0,
        LocalPlayer,
        Teammate,
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

        /// <summary>Armband-based Arena team id (-1 if unknown / no armband).</summary>
        public int TeamID = -1;

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

        /// <summary>Last realtime position written; used to detect a stale Unity worldPos cache.</summary>
        internal Vector3 LastObservedPosition;
        /// <summary>Last realtime yaw written; used together with LastObservedPosition to detect freezes.</summary>
        internal float LastObservedYaw;
        /// <summary>Number of consecutive realtime reads where Position was bit-identical to the previous tick.</summary>
        internal int IdenticalPositionTicks;
        /// <summary>Number of those identical-position ticks where yaw also changed (i.e. player is alive and moving the camera but position is frozen).</summary>
        internal int FrozenPositionTicks;

        /// <summary>
        /// Consecutive registration ticks this player has been absent from the RegisteredPlayers
        /// list. Used as a grace period so transient list-read flickers / invalid pointer hiccups
        /// don't immediately wipe a player who is still alive in the match.
        /// </summary>
        internal int MissingTicks;

        // ── Back-off timers (Environment.TickCount64) ─────────────────────
        // When non-zero, skip the corresponding init/retry until TickCount64 reaches this value.
        internal long NextTransformInitTick;
        internal long NextRotationInitTick;
        internal long NextTeamIdTick;
        internal int  TransformInitFailStreak;
        internal int  RotationInitFailStreak;
        internal int  TeamIdFailStreak;

        /// <summary>Cached ArmBand slot pointer — avoids re-scanning the equipment slots array on every TeamID read.</summary>
        internal ulong ArmBandSlotAddr;

        // ── Skeleton / bone data (written by camera worker) ───────────────

        /// <summary>Per-player skeleton (null until resolved; null for LocalPlayer).</summary>
        internal Skeleton? Skeleton;

        /// <summary>Back-off timer for skeleton init retries.</summary>
        internal long NextSkeletonInitTick;

        /// <summary>Consecutive skeleton-init failures (for exponential back-off).</summary>
        internal int SkeletonInitFailStreak;

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Name).Append(" (").Append(Type).Append(')');
            sb.Append(" @ ").Append(Position);
            sb.Append(" yaw=").Append(RotationYaw.ToString("F1")).Append('°');
            // AccountId omitted — Arena server never sends it to other clients
            if (ProfileId is not null)
                sb.Append(" prof=").Append(ProfileId);
            if (TeamID >= 0)
                sb.Append(" team=").Append((ArmbandColorType)TeamID);
            return sb.ToString();
        }
    }
}
