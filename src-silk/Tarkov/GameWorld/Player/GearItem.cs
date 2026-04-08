namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// A single piece of player equipment (armor, weapon, headwear, etc.).
    /// </summary>
    internal sealed class GearItem
    {
        /// <summary>Full item name.</summary>
        public required string Long { get; init; }

        /// <summary>Short item name.</summary>
        public required string Short { get; init; }

        /// <summary>Best rouble price (max of flea/trader).</summary>
        public int Price { get; init; }
    }
}
