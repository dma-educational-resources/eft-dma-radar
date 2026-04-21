using eft_dma_radar.Silk.Tarkov.GameWorld.Player;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// A single killfeed event read from a corpse dogtag.
    /// Immutable once created; age/fade is derived from <see cref="Timestamp"/> at render time.
    /// </summary>
    internal sealed class KillfeedEntry
    {
        /// <summary>Name of the player who made the kill.</summary>
        public string Killer { get; init; } = "";

        /// <summary>Name of the player who was killed.</summary>
        public string Victim { get; init; } = "";

        /// <summary>Weapon used (from dogtag WeaponName field).</summary>
        public string Weapon { get; init; } = "";

        /// <summary>Level of the victim (from dogtag Level field).</summary>
        public int VictimLevel { get; init; }

        /// <summary>Player type of the killer, used for colouring the entry.</summary>
        public PlayerType KillerSide { get; init; }

        /// <summary>UTC timestamp when this entry was pushed.</summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        /// <summary>Seconds elapsed since this entry was created.</summary>
        public double AgeSec => (DateTime.UtcNow - Timestamp).TotalSeconds;

        /// <summary>
        /// Formatted display line for this entry.
        /// e.g. "Nikita [L72] ► John [M4A1]"
        /// </summary>
        public string FormatDisplay()
        {
            var sb = new System.Text.StringBuilder(64);
            sb.Append(Killer);
            if (VictimLevel > 0)
            {
                sb.Append(" [L");
                sb.Append(VictimLevel);
                sb.Append(']');
            }
            sb.Append(" \u25ba ");  // ►
            sb.Append(Victim);
            if (!string.IsNullOrWhiteSpace(Weapon))
            {
                sb.Append(" [");
                sb.Append(Weapon);
                sb.Append(']');
            }
            return sb.ToString();
        }
    }
}
