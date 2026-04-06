namespace eft_dma_radar.Silk.Tarkov
{
    /// <summary>
    /// Minimal player representation for Phase 1.
    /// Contains only what's needed to render a dot + direction arrow on the radar.
    /// </summary>
    public class PlayerBase
    {
        public string Name { get; init; } = string.Empty;
        public PlayerType Type { get; init; }
        public Vector3 Position { get; set; }
        public float RotationYaw { get; set; }
        public int GroupID { get; set; }
        public int SpawnGroupID { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public bool IsLocalPlayer { get; init; }

        public bool IsHuman => Type is PlayerType.Default or PlayerType.Teammate
            or PlayerType.USEC or PlayerType.BEAR or PlayerType.PScav
            or PlayerType.Streamer or PlayerType.SpecialPlayer;

        public bool IsHostile => IsHuman && Type is not PlayerType.Teammate;

        public override string ToString() => $"{Type} [{Name}]";
    }

    /// <summary>
    /// Player type enum — mirrors the WPF project's PlayerType for compatibility.
    /// </summary>
    public enum PlayerType
    {
        Default,
        Teammate,
        USEC,
        BEAR,
        AIScav,
        AIRaider,
        AIBoss,
        PScav,
        SpecialPlayer,
        Streamer
    }
}
