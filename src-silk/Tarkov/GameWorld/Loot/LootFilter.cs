using eft_dma_radar.Silk.Config;
using eft_dma_radar.Silk.Misc.Data;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Central loot filter logic. Determines visibility and importance for each loot item
    /// based on config settings and runtime search state.
    /// </summary>
    internal static class LootFilter
    {
        // ── Runtime state (not persisted) ────────────────────────────────────

        /// <summary>Runtime name search text. Empty = no name filter.</summary>
        private static string _searchText = string.Empty;

        /// <summary>Ref-return so ImGui can bind directly.</summary>
        public static ref string SearchText => ref _searchText;

        /// <summary>Number of items that passed the filter on the last frame.</summary>
        public static int VisibleCount { get; private set; }

        /// <summary>Total items evaluated on the last frame.</summary>
        public static int TotalCount { get; private set; }

        // ── Constants ────────────────────────────────────────────────────────

        /// <summary>Price source labels for the UI combo box.</summary>
        public static readonly string[] PriceSourceLabels = ["Best", "Flea Market", "Trader"];

        /// <summary>Quick-set min price presets (roubles).</summary>
        public static readonly (string Label, int Value)[] MinPricePresets =
        [
            ("Any",  0),
            ("10K",  10_000),
            ("25K",  25_000),
            ("50K",  50_000),
            ("100K", 100_000),
            ("200K", 200_000),
        ];

        // ── Price ────────────────────────────────────────────────────────────

        /// <summary>
        /// Effective display price for an item, respecting price source and price-per-slot.
        /// </summary>
        public static int GetDisplayPrice(TarkovMarketItem item)
        {
            var config = SilkProgram.Config;

            long raw = config.LootPriceSource switch
            {
                1 => item.FleaPrice > 0 ? item.FleaPrice : item.TraderPrice,
                2 => item.TraderPrice > 0 ? item.TraderPrice : item.FleaPrice,
                _ => Math.Max(item.FleaPrice, item.TraderPrice),
            };

            if (config.LootPricePerSlot)
                raw /= item.GridCount;

            return (int)raw;
        }

        // ── Filtering ────────────────────────────────────────────────────────

        /// <summary>
        /// Whether this item should be drawn on the radar.
        /// </summary>
        public static bool ShouldDraw(TarkovMarketItem item, int displayPrice)
        {
            // Price gate
            if (displayPrice < SilkProgram.Config.LootMinPrice)
                return false;

            // Name search
            if (_searchText.Length > 0)
            {
                if (!item.ShortName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether this item is highlighted as important (green).
        /// </summary>
        public static bool IsImportant(int displayPrice) =>
            displayPrice >= SilkProgram.Config.LootImportantPrice;

        // ── Batch helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Directly set <see cref="VisibleCount"/> and <see cref="TotalCount"/>.
        /// Called from the render loop which already evaluates ShouldDraw per item.
        /// </summary>
        public static void SetCounts(int visible, int total)
        {
            VisibleCount = visible;
            TotalCount = total;
        }

        /// <summary>
        /// Clear the runtime search text.
        /// </summary>
        public static void ClearSearch() => _searchText = string.Empty;

        // ── Price formatting cache ───────────────────────────────────────
        // FormatPrice is called from tooltips and widgets — caching avoids repeated
        // string interpolation for the same price value across frames.
        private static readonly ConcurrentDictionary<int, string> _priceCache = new();

        /// <summary>
        /// Format a rouble price for display (e.g. 1.2M, 50K, 999).
        /// Results are cached to avoid per-frame string allocation.
        /// </summary>
        public static string FormatPrice(int price) =>
            _priceCache.GetOrAdd(price, static p => p switch
            {
                >= 1_000_000 => $"{p / 1_000_000f:0.#}M",
                >= 1_000 => $"{p / 1_000f:0.#}K",
                _ => p.ToString(),
            });

        /// <summary>
        /// Reset all filter settings to defaults.
        /// </summary>
        public static void ResetAll()
        {
            var config = SilkProgram.Config;
            config.LootMinPrice = 50_000;
            config.LootImportantPrice = 200_000;
            config.LootPriceSource = 0;
            config.LootPricePerSlot = false;
            config.ShowLoot = true;
            ClearSearch();
        }
    }
}
