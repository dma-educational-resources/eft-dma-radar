using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Unity.LowLevel
{
    /// <summary>
    /// Contains Cache Data for Unity Low Level API.
    /// </summary>
    public sealed class LowLevelCache
    {
        [JsonPropertyName("ZK8MQLY")]
        public uint PID { get; set; }

        [JsonPropertyName("X8kP9Ln")]
        public ulong CodeCave { get; set; }

        [JsonPropertyName("tZ6Yv7m")]
        public ulong UnityPlayerDll { get; set; }

        [JsonPropertyName("K9XrF2q")]
        public ulong MonoDll { get; set; }

        [JsonPropertyName("XJ9TQWZ")]
        public ulong HookedMonoFuncAddress { get; set; }

        [JsonPropertyName("PL6VDRM")]
        public ulong HookedMonoFunc { get; set; }

        [JsonPropertyName("L3Wp7Tz")]
        public ConcurrentDictionary<int, CachedChamsMaterial> ChamsMaterialCache { get; set; } = new();

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
            CodeCave = default;
            UnityPlayerDll = default;
            MonoDll = default;
            HookedMonoFuncAddress = default;
            HookedMonoFunc = default;
            ChamsMaterialCache.Clear();
            LootChamsCache.Clear();
            PlayerChamsCache.Clear();
        }
    }

    public class CachedChamsMaterial
    {
        public int InstanceID { get; set; }
        public ulong Address { get; set; }
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