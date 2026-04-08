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
        }
    }
}
