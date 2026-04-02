using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Tarkov.Unity.LowLevel
{
    /// <summary>
    /// Contains Cache Data for Unity Low Level API.
    /// </summary>
    public sealed class LowLevelCache
    {
        [JsonPropertyName("ZK8MQLY")]
        public uint PID { get; set; }

        [JsonPropertyName("tZ6Yv7m")]
        public ulong UnityPlayerDll { get; set; }

        [JsonPropertyName("K9XrF2q")]
        public ulong MonoDll { get; set; }

        [JsonPropertyName("lootChamsCache")]
        public ConcurrentDictionary<string, CachedLootMaterial> LootChamsCache { get; private set; } = new();

        [JsonPropertyName("playerChamsCache")]
        public ConcurrentDictionary<ulong, CachedPlayerMaterials> PlayerChamsCache { get; private set; } = new();

        /// <summary>
        /// Persist the cache to disk.
        /// </summary>
        public async Task SaveAsync() => await SharedProgram.Config.SaveAsync();

        /// <summary>
        /// Reset the cache to defaults.
        /// </summary>
        public void Reset()
        {
            UnityPlayerDll = default;
            MonoDll = default;
            LootChamsCache.Clear();
            PlayerChamsCache.Clear();
        }
    }

    public sealed class CachedLootMaterial
    {
        [JsonPropertyName("itemId")]
        public string ItemId { get; init; }

        [JsonPropertyName("originalMaterials")]
        public Dictionary<int, int> OriginalMaterials { get; init; } = new();

        [JsonPropertyName("cacheTime")]
        public DateTime CacheTime { get; init; }
    }

    public sealed class CachedPlayerMaterials
    {
        [JsonPropertyName("playerBase")]
        public ulong PlayerBase { get; init; }

        [JsonPropertyName("playerName")]
        public string PlayerName { get; init; }

        [JsonPropertyName("clothingMaterials")]
        public Dictionary<string, Dictionary<int, int>> ClothingMaterials { get; init; } = new();

        [JsonPropertyName("gearMaterials")]
        public Dictionary<string, Dictionary<int, int>> GearMaterials { get; init; } = new();

        [JsonPropertyName("cacheTime")]
        public DateTime CacheTime { get; init; }
    }
}