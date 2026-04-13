using System.Collections.Frozen;

namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// Loads and caches the Tarkov item database from the embedded DEFAULT_DATA.json resource.
    /// Call <see cref="ModuleInit"/> once at startup.
    /// </summary>
    internal static class EftDataManager
    {
        /// <summary>
        /// All items keyed by BSG ID.
        /// </summary>
        public static FrozenDictionary<string, TarkovMarketItem> AllItems { get; private set; }
            = FrozenDictionary<string, TarkovMarketItem>.Empty;

        /// <summary>
        /// Map data (extracts, transits) keyed by map nameId.
        /// </summary>
        public static FrozenDictionary<string, MapElement> MapData { get; private set; }
            = FrozenDictionary<string, MapElement>.Empty;

        /// <summary>
        /// Loads the item database from the embedded DEFAULT_DATA.json resource.
        /// </summary>
        internal static void ModuleInit()
        {
            const string resourceName = "eft_dma_radar.Silk.DEFAULT_DATA.json";
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    Log.WriteLine($"[EftDataManager] Embedded resource '{resourceName}' not found.");
                    return;
                }

                var data = JsonSerializer.Deserialize<DataRoot>(stream, _jsonOpts);
                if (data?.Items is null || data.Items.Count == 0)
                {
                    Log.WriteLine("[EftDataManager] No items found in data file.");
                    return;
                }

                var builder = new Dictionary<string, TarkovMarketItem>(data.Items.Count, StringComparer.Ordinal);
                foreach (var item in data.Items)
                {
                    if (!string.IsNullOrEmpty(item.BsgId))
                        builder.TryAdd(item.BsgId, item);
                }

                AllItems = builder.ToFrozenDictionary(StringComparer.Ordinal);
                Log.WriteLine($"[EftDataManager] Loaded {AllItems.Count} items.");

                // Load map data (extracts + transits)
                if (data.Maps is { Count: > 0 })
                {
                    var mapBuilder = new Dictionary<string, MapElement>(data.Maps.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var map in data.Maps)
                    {
                        if (!string.IsNullOrEmpty(map.NameId))
                            mapBuilder.TryAdd(map.NameId, map);
                    }
                    MapData = mapBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
                    Log.WriteLine($"[EftDataManager] Loaded {MapData.Count} map configs.");
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[EftDataManager] Failed to load data: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private sealed class DataRoot
        {
            [JsonPropertyName("items")]
            public List<TarkovMarketItem> Items { get; set; } = [];

            [JsonPropertyName("maps")]
            public List<MapElement> Maps { get; set; } = [];
        }

        #region Map Data Models

        /// <summary>
        /// Map data element containing extracts and transits.
        /// </summary>
        internal sealed class MapElement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("nameId")]
            public string NameId { get; set; } = string.Empty;

            [JsonPropertyName("transits")]
            public List<TransitElement> Transits { get; set; } = [];
        }

        internal sealed class TransitElement
        {
            [JsonPropertyName("description")]
            public string Description { get; set; } = string.Empty;

            [JsonPropertyName("position")]
            public MapPositionElement? Position { get; set; }
        }

        internal sealed class MapPositionElement
        {
            [JsonPropertyName("x")]
            public float X { get; set; }

            [JsonPropertyName("y")]
            public float Y { get; set; }

            [JsonPropertyName("z")]
            public float Z { get; set; }

            public Vector3 ToVector3() => new(X, Y, Z);
        }

        #endregion
    }
}
