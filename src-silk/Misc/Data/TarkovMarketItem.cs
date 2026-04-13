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

        // ── Category helpers (match against the Categories array) ────────────

        [JsonIgnore] public bool IsMeds => HasCategory("Meds");
        [JsonIgnore] public bool IsFood => HasCategory("Food and drink");
        [JsonIgnore] public bool IsBackpack => HasCategory("Backpack");
        [JsonIgnore] public bool IsKey => HasCategory("Key");
        [JsonIgnore] public bool IsAmmo => HasCategory("Ammo");
        [JsonIgnore] public bool IsBarter => HasCategory("Barter item");
        [JsonIgnore] public bool IsWeapon => HasCategory("Weapon");
        [JsonIgnore] public bool IsWeaponMod => HasCategory("Weapon mod");
        [JsonIgnore] public bool IsCurrency => HasCategory("Money");

        private bool HasCategory(string category)
        {
            for (int i = 0; i < Categories.Length; i++)
            {
                if (Categories[i].Equals(category, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
