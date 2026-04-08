namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// Minimal item data from the Tarkov market database.
    /// </summary>
    internal sealed class TarkovMarketItem
    {
        [JsonPropertyName("bsgID")]
        public string BsgId { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("shortName")]
        public string ShortName { get; init; } = string.Empty;

        [JsonPropertyName("price")]
        public long TraderPrice { get; init; }

        [JsonPropertyName("fleaPrice")]
        public long FleaPrice { get; init; }

        [JsonPropertyName("slots")]
        public int Slots { get; init; } = 1;

        [JsonPropertyName("categories")]
        public string[] Categories { get; init; } = [];

        /// <summary>Best price (max of flea and trader).</summary>
        [JsonIgnore]
        public int BestPrice => (int)Math.Max(FleaPrice, TraderPrice);

        /// <summary>Number of grid slots (at least 1).</summary>
        [JsonIgnore]
        public int GridCount => Slots < 1 ? 1 : Slots;

        /// <summary>Check whether this item belongs to the given category.</summary>
        public bool HasCategory(string category) =>
            Array.Exists(Categories, c => c.Equals(category, StringComparison.OrdinalIgnoreCase));
    }
}
