namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Central loot filter logic. Determines visibility, importance, and highlight state
    /// for each loot item based on config settings, category toggles, wishlist/blacklist,
    /// and runtime search state.
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

        // ── Persistent filter data (wishlist / blacklist) ────────────────────

        private static LootFilterData _filterData = new();

        /// <summary>The persistent wishlist/blacklist data.</summary>
        public static LootFilterData FilterData => _filterData;

        /// <summary>Load loot filter data from disk. Call once at startup.</summary>
        public static void LoadFilterData()
        {
            _filterData = LootFilterData.Load();
        }

        /// <summary>Save loot filter data to disk.</summary>
        public static void SaveFilterData()
        {
            _filterData.Save();
        }

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

        // ── Filter result ────────────────────────────────────────────────────

        /// <summary>
        /// Result of evaluating an item against all filter criteria.
        /// Avoids repeated evaluation across render + widget + tooltip paths.
        /// </summary>
        internal readonly struct FilterResult
        {
            /// <summary>Whether the item should be drawn on the radar.</summary>
            public bool Visible { get; init; }

            /// <summary>Whether the item is highlighted as important (high value).</summary>
            public bool Important { get; init; }

            /// <summary>Whether the item is on the wishlist.</summary>
            public bool Wishlisted { get; init; }

            /// <summary>Whether the item was shown due to a category toggle.</summary>
            public bool CategoryMatch { get; init; }

            /// <summary>Whether the item is required for an active quest.</summary>
            public bool QuestRequired { get; init; }

            public static readonly FilterResult Hidden = new() { Visible = false };
        }

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
        /// Full evaluation of an item against all filter criteria.
        /// Returns a <see cref="FilterResult"/> with visibility, importance, and highlight info.
        /// </summary>
        public static FilterResult Evaluate(TarkovMarketItem item, int displayPrice)
        {
            var config = SilkProgram.Config;

            // Blacklist — always hidden
            if (_filterData.IsBlacklisted(item.BsgId))
                return FilterResult.Hidden;

            // Wishlist — always visible if enabled
            bool wishlisted = config.LootShowWishlist && _filterData.IsWishlisted(item.BsgId);
            if (wishlisted)
            {
                return new FilterResult
                {
                    Visible = true,
                    Important = IsImportant(displayPrice),
                    Wishlisted = true,
                };
            }

            // Quest items — always visible if quests are enabled and item is needed
            if (config.ShowQuests)
            {
                var qm = Memory.QuestManager;
                if (qm is not null && qm.IsItemRequired(item.BsgId))
                {
                    return new FilterResult
                    {
                        Visible = true,
                        Important = true,
                        QuestRequired = true,
                    };
                }
            }

            // Category toggle bypass (show regardless of price)
            bool categoryMatch = IsCategoryMatch(item, config);
            if (categoryMatch)
            {
                // Name search still applies to category items
                if (!PassesNameSearch(item))
                    return FilterResult.Hidden;

                return new FilterResult
                {
                    Visible = true,
                    Important = IsImportant(displayPrice),
                    CategoryMatch = true,
                };
            }

            // Standard price + name filter
            if (displayPrice < config.LootMinPrice)
                return FilterResult.Hidden;

            if (!PassesNameSearch(item))
                return FilterResult.Hidden;

            return new FilterResult
            {
                Visible = true,
                Important = IsImportant(displayPrice),
            };
        }

        /// <summary>
        /// Simplified visibility check (backward-compatible).
        /// </summary>
        public static bool ShouldDraw(TarkovMarketItem item, int displayPrice) =>
            Evaluate(item, displayPrice).Visible;

        /// <summary>
        /// Whether this item is highlighted as important (green).
        /// </summary>
        public static bool IsImportant(int displayPrice) =>
            displayPrice >= SilkProgram.Config.LootImportantPrice;

        // ── Internal helpers ─────────────────────────────────────────────────

        private static bool PassesNameSearch(TarkovMarketItem item)
        {
            if (_searchText.Length == 0)
                return true;

            return item.ShortName.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                   item.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCategoryMatch(TarkovMarketItem item, SilkConfig config)
        {
            if (config.LootShowMeds && item.IsMeds) return true;
            if (config.LootShowFood && item.IsFood) return true;
            if (config.LootShowBackpacks && item.IsBackpack) return true;
            if (config.LootShowKeys && item.IsKey) return true;
            return false;
        }

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
            config.LootShowMeds = false;
            config.LootShowFood = false;
            config.LootShowBackpacks = false;
            config.LootShowKeys = false;
            config.LootShowWishlist = true;
            ClearSearch();
        }

        /// <summary>
        /// Clears runtime caches (price formatting, etc.) to free memory between raids.
        /// </summary>
        public static void ClearCaches()
        {
            _priceCache.Clear();
        }
    }
}
