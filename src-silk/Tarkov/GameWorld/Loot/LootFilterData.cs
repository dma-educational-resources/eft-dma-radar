using System.Collections.Frozen;
using System.IO;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Persistent wishlist and blacklist data for loot filtering.
    /// Stored separately from the main config at %AppData%\eft-dma-radar-silk\lootfilters.json.
    /// </summary>
    internal sealed class LootFilterData
    {
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "eft-dma-radar-silk",
            "lootfilters.json");

        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        // ── Serialized state ─────────────────────────────────────────────────

        /// <summary>BSG IDs of wishlisted items — always shown, highlighted in cyan.</summary>
        [JsonPropertyName("wishlist")]
        public List<string> Wishlist { get; set; } = [];

        /// <summary>BSG IDs of blacklisted items — never shown regardless of price.</summary>
        [JsonPropertyName("blacklist")]
        public List<string> Blacklist { get; set; } = [];

        // ── Fast lookup sets (rebuilt after any mutation) ─────────────────────

        [JsonIgnore]
        private FrozenSet<string> _wishlistSet = FrozenSet<string>.Empty;

        [JsonIgnore]
        private FrozenSet<string> _blacklistSet = FrozenSet<string>.Empty;

        /// <summary>O(1) check: is this BSG ID wishlisted?</summary>
        public bool IsWishlisted(string bsgId) => _wishlistSet.Contains(bsgId);

        /// <summary>O(1) check: is this BSG ID blacklisted?</summary>
        public bool IsBlacklisted(string bsgId) => _blacklistSet.Contains(bsgId);

        /// <summary>Rebuild frozen lookup sets from the mutable lists.</summary>
        public void RebuildSets()
        {
            _wishlistSet = Wishlist.ToFrozenSet(StringComparer.Ordinal);
            _blacklistSet = Blacklist.ToFrozenSet(StringComparer.Ordinal);
        }

        // ── Mutation helpers ─────────────────────────────────────────────────

        /// <summary>Add an item to the wishlist (removes from blacklist if present).</summary>
        public bool AddToWishlist(string bsgId)
        {
            if (Wishlist.Contains(bsgId))
                return false;
            Blacklist.Remove(bsgId);
            Wishlist.Add(bsgId);
            RebuildSets();
            return true;
        }

        /// <summary>Add an item to the blacklist (removes from wishlist if present).</summary>
        public bool AddToBlacklist(string bsgId)
        {
            if (Blacklist.Contains(bsgId))
                return false;
            Wishlist.Remove(bsgId);
            Blacklist.Add(bsgId);
            RebuildSets();
            return true;
        }

        /// <summary>Remove an item from the wishlist.</summary>
        public bool RemoveFromWishlist(string bsgId)
        {
            bool removed = Wishlist.Remove(bsgId);
            if (removed)
                RebuildSets();
            return removed;
        }

        /// <summary>Remove an item from the blacklist.</summary>
        public bool RemoveFromBlacklist(string bsgId)
        {
            bool removed = Blacklist.Remove(bsgId);
            if (removed)
                RebuildSets();
            return removed;
        }

        // ── Persistence ──────────────────────────────────────────────────────

        /// <summary>Load from disk, or return a new empty instance.</summary>
        public static LootFilterData Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<LootFilterData>(json) ?? new();
                    data.RebuildSets();
                    Log.WriteLine($"[LootFilterData] Loaded {data.Wishlist.Count} wishlisted, {data.Blacklist.Count} blacklisted items.");
                    return data;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootFilterData] Failed to load, using defaults: {ex.Message}");
            }

            return new LootFilterData();
        }

        /// <summary>Persist to disk.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                var json = JsonSerializer.Serialize(this, _jsonOpts);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[LootFilterData] Failed to save: {ex.Message}");
            }
        }
    }
}
