using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Misc.Data
{
    /// <summary>
    /// Class JSON Representation of Tarkov Market Data.
    /// </summary>
    public class TarkovMarketItem
    {
        /// <summary>
        /// Item ID.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("bsgID")]
        public string BsgId { get; init; } = "NULL";
        /// <summary>
        /// Item Full Name.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("name")]
        public string Name { get; init; } = "NULL";
        /// <summary>
        /// Item Short Name.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("shortName")]
        public string ShortName { get; init; } = "NULL";
        /// <summary>
        /// Highest Vendor Price.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("price")]
        public long TraderPrice { get; init; } = 0;
        /// <summary>
        /// Optimal Flea Market Price.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("fleaPrice")]
        public long FleaPrice { get; init; } = 0;
        /// <summary>
        /// Number of slots taken up in the inventory.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("slots")]
        public int Slots { get; init; } = 1;
        [JsonInclude]
        [JsonPropertyName("categories")]
        public IReadOnlyList<string> Tags { get; init; } = new List<string>();
        /// <summary>
        /// URL to the item's main icon.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("iconLink")]
        public string IconLink { get; init; } = string.Empty;

        /// <summary>
        /// Fallback icon URL if the main icon fails.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("iconLinkFallback")]
        public string IconLinkFallback { get; init; } = string.Empty;

        /// <summary>
        /// URL to the item's full-size image.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("imageLink")]
        public string ImageLink { get; init; } = string.Empty;

        /// <summary>
        /// Weapon caliber if applicable.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("caliber")]
        public string Caliber { get; init; } = null;

        /// <summary>
        /// True if this item is Important via the Filters.
        /// </summary>
        [JsonIgnore]
        public bool Important => CustomFilter?.Important ?? false;
        /// <summary>
        /// True if this item is Blacklisted via the Filters.
        /// </summary>
        [JsonIgnore]
        public bool Blacklisted => CustomFilter?.Blacklisted ?? false;
        /// <summary>
        /// Is a Medical Item.
        /// </summary>
        [JsonIgnore]
        public bool IsMed => Tags.Contains("Meds");
        /// <summary>
        /// Is a Food Item.
        /// </summary>
        [JsonIgnore]
        public bool IsFood => Tags.Contains("Food and drink");
        /// <summary>
        /// Is a backpack.
        /// </summary>
        [JsonIgnore]
        public bool IsBackpack => Tags.Contains("Backpack");
        /// <summary>
        /// Is a Weapon Item.
        /// </summary>
        [JsonIgnore]
        public bool IsWeapon => Tags.Contains("Weapon");
        /// <summary>
        /// Is a Throwable Item.
        /// </summary>
        [JsonIgnore]
        public bool IsThrowableWeapon => Tags.Contains("Throwable weapon");
        /// <summary>
        /// Is a Weapon Mod.
        /// </summary>
        [JsonIgnore]
        public bool IsWeaponMod => Tags.Contains("Weapon mod");
        /// <summary>
        /// Is Currency (Roubles,etc.)
        /// </summary>
        [JsonIgnore]
        public bool IsCurrency => Tags.Contains("Money");
        /// <summary>
        /// IsAmmo
        /// </summary>
        [JsonIgnore]
        public bool IsAmmo => Tags.Contains("Ammo");

        /// <summary>
        /// This field is set if this item has a special filter.
        /// </summary>
        [JsonIgnore]
        public LootFilterEntry CustomFilter { get; private set; }

        /// <summary>
        /// Set the Custom Filter for this item.
        /// </summary>
        public void SetFilter(LootFilterEntry filter)
        {
            if (filter?.Enabled ?? false)
                CustomFilter = filter;
            else
                CustomFilter = null;
        }

        public override string ToString() => Name;

        /// <summary>
        /// Format price numeral as a string.
        /// </summary>
        /// <param name="price">Price to convert to string format.</param>
        public static string FormatPrice(int price)
        {
            if (price >= 1000000)
                return (price / 1000000D).ToString("0.##") + "M";
            if (price >= 1000)
                return (price / 1000D).ToString("0") + "K";

            return price.ToString();
        }
    }
}
