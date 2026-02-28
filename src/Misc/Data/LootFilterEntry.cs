using SkiaSharp;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Misc.Data
{
    /// <summary>
    /// JSON Wrapper for Important Loot.
    /// </summary>
    public sealed class LootFilterEntry
    {
        [JsonIgnore]
        public static readonly IReadOnlyList<EntryType> Types = new List<EntryType>()
        {
            new()
            {
                Id = 0,
                Name = "Important Loot"
            },
            new()
            {
                Id = 1,
                Name = "Blacklisted Loot"
            }
        };
        /// <summary>
        /// Item's BSG ID.
        /// </summary>
        [JsonPropertyName("itemID")]
        public string ItemID { get; set; } = string.Empty;
        /// <summary>
        /// True if this entry is Enabled/Active.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
        /// <summary>
        /// Entry Type.
        /// 0 = Important Loot
        /// 1 = Blacklisted Loot
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; } = 0;
        /// <summary>
        /// True if this filter represents an Important Item.
        /// </summary>
        [JsonIgnore]
        public bool Important => Type == 0;
        /// <summary>
        /// True if this filter represents a Blacklisted Item.
        /// </summary>
        [JsonIgnore]
        public bool Blacklisted => Type == 1;
        [JsonIgnore]
        private string _name;
        /// <summary>
        /// Item Long Name per Tarkov Market.
        /// </summary>
        [JsonIgnore]
        public string Name
        {
            get
            {
                _name ??= EftDataManager.AllItems?
                    .FirstOrDefault(x => x.Key.Equals(ItemID, StringComparison.OrdinalIgnoreCase))
                    .Value?.Name;
                return _name ?? "NULL";
            }
        }
        /// <summary>
        /// Entry Comment (name of item,etc.)
        /// </summary>
        [JsonPropertyName("comment")]
        public string Comment { get; set; } = string.Empty;
        /// <summary>
        /// Hex value of the rgba color.
        /// </summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = SKColors.Turquoise.ToString();

        public sealed class EntryType
        {
            /// <summary>
            /// Entry Type.
            /// 0 = Important Loot
            /// 1 = Blacklisted Loot
            /// </summary>
            public int Id { get; init; }
            /// <summary>
            /// Type Name.
            /// </summary>
            public string Name { get; init; }

            public override string ToString() => Name;
        }
    }   
}
