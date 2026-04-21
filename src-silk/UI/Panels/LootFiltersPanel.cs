using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Loot Filters Panel — search, price thresholds, price mode, category toggles,
    /// wishlist/blacklist management, quick presets, and live stats.
    /// </summary>
    internal static class LootFiltersPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>
        /// Whether the loot filters panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        // ── Wishlist/Blacklist item search state ─────────────────────────────

        private static string _itemSearchText = string.Empty;
        private static readonly List<TarkovMarketItem> _searchResults = new(20);
        private static bool _searchDirty;
        private const int MaxSearchResults = 20;

        /// <summary>
        /// Draw the loot filters panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u25a3 Loot Filters", ref isOpen, new Vector2(420, 560));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawStatusBar();
            ImGui.Spacing();
            DrawSearchSection();
            DrawPriceSection();
            DrawCategorySection();
            DrawWishlistSettingsSection();
            DrawIngameWishlistSection();
            DrawWishlistBlacklistSection();
            DrawOptionsSection();
            DrawFooter();
        }

        // ── Status bar ───────────────────────────────────────────────────────

        private static void DrawStatusBar()
        {
            // Show loot toggle + live item count on a single line
            bool showLoot = Config.ShowLoot;
            if (ImGui.Checkbox("Show Loot", ref showLoot))
                Config.ShowLoot = showLoot;

            ImGui.SameLine();
            var visible = LootFilter.VisibleCount;
            var total = LootFilter.TotalCount;
            string stats = total > 0 ? $"{visible}/{total} items" : "No loot data";

            // Right-align the stats text
            float textWidth = ImGui.CalcTextSize(stats).X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - textWidth - ImGui.GetStyle().WindowPadding.X);

            if (visible == 0 && total > 0)
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), stats);
            else
                ImGui.TextDisabled(stats);
        }

        // ── Search ───────────────────────────────────────────────────────────

        private static void DrawSearchSection()
        {
            ImGui.SeparatorText("Search");

            ImGui.SetNextItemWidth(-70);
            ImGui.InputTextWithHint("##LootSearch", "Search by name...", ref LootFilter.SearchText, 128);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Filter loot by item name or short name (case-insensitive)");

            ImGui.SameLine();
            if (ImGui.Button("Clear", new Vector2(-1, 0)))
                LootFilter.ClearSearch();
        }

        // ── Price ────────────────────────────────────────────────────────────

        private static void DrawPriceSection()
        {
            ImGui.SeparatorText("Price");

            // Quick preset buttons
            ImGui.TextDisabled("Quick Set Min:");
            ImGui.SameLine();
            for (int i = 0; i < LootFilter.MinPricePresets.Length; i++)
            {
                var (label, value) = LootFilter.MinPricePresets[i];
                if (i > 0) ImGui.SameLine();

                bool isActive = Config.LootMinPrice == value;
                if (isActive)
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                if (ImGui.SmallButton(label))
                    Config.LootMinPrice = value;

                if (isActive)
                    ImGui.PopStyleColor();
            }

            // Min price slider (with right-click to type)
            int minPrice = Config.LootMinPrice;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragInt("##MinPrice", ref minPrice, 1000f, 0, 2_000_000, "Min: %d \u20bd"))
                Config.LootMinPrice = Math.Max(0, minPrice);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hide loot worth less than this amount\nDrag or Ctrl+Click to type");

            // Important price slider
            int importantPrice = Config.LootImportantPrice;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.DragInt("##ImportantPrice", ref importantPrice, 1000f, 0, 5_000_000, "Important: %d \u20bd"))
                Config.LootImportantPrice = Math.Max(0, importantPrice);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Highlight loot worth at least this amount in green\nDrag or Ctrl+Click to type");

            // Validation hint
            if (Config.LootMinPrice > Config.LootImportantPrice && Config.LootImportantPrice > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.8f, 0.3f, 1f));
                ImGui.TextWrapped("\u26a0 Min price is above important price \u2014 no items will highlight as important.");
                ImGui.PopStyleColor();
            }
        }

        // ── Categories ───────────────────────────────────────────────────────

        private static void DrawCategorySection()
        {
            ImGui.SeparatorText("Categories");
            ImGui.TextDisabled("Always show items in these categories (bypasses price filter):");

            // Row 1
            bool showMeds = Config.LootShowMeds;
            if (ImGui.Checkbox("\u271a Meds", ref showMeds))
                Config.LootShowMeds = showMeds;

            ImGui.SameLine(0, 20);
            bool showFood = Config.LootShowFood;
            if (ImGui.Checkbox("\u2615 Food", ref showFood))
                Config.LootShowFood = showFood;

            ImGui.SameLine(0, 20);
            bool showBP = Config.LootShowBackpacks;
            if (ImGui.Checkbox("\u25c8 Backpacks", ref showBP))
                Config.LootShowBackpacks = showBP;

            // Row 2
            bool showKeys = Config.LootShowKeys;
            if (ImGui.Checkbox("\u26bf Keys", ref showKeys))
                Config.LootShowKeys = showKeys;

            ImGui.SameLine(0, 20);
            bool showWL = Config.LootShowWishlist;
            if (ImGui.Checkbox("\u2605 Wishlist", ref showWL))
                Config.LootShowWishlist = showWL;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show wishlisted items regardless of price filter");
        }

        // ── Wishlist Settings (in-game wishlist groups) ──────────────────────

        private static readonly string[] _groupLabels =
        [
            "Quests", "Hideout", "Trading", "Equipment", "Other",
        ];

        private static void DrawWishlistSettingsSection()
        {
            if (!ImGui.CollapsingHeader("Wishlist Settings"))
                return;

            bool showWL = Config.LootShowWishlist;
            if (ImGui.Checkbox("Show wishlisted items", ref showWL))
                Config.LootShowWishlist = showWL;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Always show wishlisted items, bypassing price/category filters.");

            bool useIngame = Config.LootUseIngameWishlist;
            if (ImGui.Checkbox("Use in-game wishlist", ref useIngame))
                Config.LootUseIngameWishlist = useIngame;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Include items you marked as favourites inside Tarkov itself\n(read live from the in-game WishlistManager).");

            if (!Config.LootUseIngameWishlist)
                ImGui.BeginDisabled();

            ImGui.TextDisabled("Include groups:");

            // 3 + 2 layout
            bool gQuests = Config.LootWishlistGroupQuests;
            if (ImGui.Checkbox("Quests", ref gQuests)) Config.LootWishlistGroupQuests = gQuests;
            ImGui.SameLine(0, 16);
            bool gHideout = Config.LootWishlistGroupHideout;
            if (ImGui.Checkbox("Hideout", ref gHideout)) Config.LootWishlistGroupHideout = gHideout;
            ImGui.SameLine(0, 16);
            bool gTrading = Config.LootWishlistGroupTrading;
            if (ImGui.Checkbox("Trading", ref gTrading)) Config.LootWishlistGroupTrading = gTrading;

            bool gEquip = Config.LootWishlistGroupEquipment;
            if (ImGui.Checkbox("Equipment", ref gEquip)) Config.LootWishlistGroupEquipment = gEquip;
            ImGui.SameLine(0, 16);
            bool gOther = Config.LootWishlistGroupOther;
            if (ImGui.Checkbox("Other", ref gOther)) Config.LootWishlistGroupOther = gOther;

            if (!Config.LootUseIngameWishlist)
                ImGui.EndDisabled();
        }

        // ── In-game Wishlist (live, read-only, grouped) ──────────────────────

        private static void DrawIngameWishlistSection()
        {
            var wm = Memory.WishlistManager;
            int totalCount = wm?.Items.Count ?? 0;

            if (!ImGui.CollapsingHeader($"In-game Wishlist ({totalCount})"))
                return;

            if (wm is null || totalCount == 0)
            {
                ImGui.TextDisabled("Not in raid or wishlist is empty.");
                return;
            }

            // Bucket items by group for a cleaner display.
            var buckets = new List<string>[5];
            for (int i = 0; i < 5; i++)
                buckets[i] = new List<string>(8);

            foreach (var kvp in wm.Items)
            {
                int g = kvp.Value;
                if (g >= 0 && g < 5)
                    buckets[g].Add(kvp.Key);
            }

            for (int g = 0; g < 5; g++)
            {
                var list = buckets[g];
                if (list.Count == 0)
                    continue;

                bool enabled = g switch
                {
                    0 => Config.LootWishlistGroupQuests,
                    1 => Config.LootWishlistGroupHideout,
                    2 => Config.LootWishlistGroupTrading,
                    3 => Config.LootWishlistGroupEquipment,
                    4 => Config.LootWishlistGroupOther,
                    _ => true,
                };

                string header = $"{_groupLabels[g]} ({list.Count}){(enabled ? string.Empty : " — hidden")}##wlg{g}";
                if (!ImGui.TreeNodeEx(header))
                    continue;

                list.Sort(static (a, b) =>
                    string.Compare(
                        EftDataManager.AllItems.TryGetValue(a, out var ia) ? ia.ShortName : a,
                        EftDataManager.AllItems.TryGetValue(b, out var ib) ? ib.ShortName : b,
                        StringComparison.OrdinalIgnoreCase));

                for (int i = 0; i < list.Count; i++)
                {
                    var bsgId = list[i];
                    string name = ResolveItemName(bsgId);
                    if (EftDataManager.AllItems.TryGetValue(bsgId, out var item) && item.BestPrice > 0)
                        ImGui.BulletText($"{name}  ({LootFilter.FormatPrice(item.BestPrice)})");
                    else
                        ImGui.BulletText(name);
                }

                ImGui.TreePop();
            }
        }

        // ── Wishlist / Blacklist ─────────────────────────────────────────────

        private static void DrawWishlistBlacklistSection()
        {
            if (!ImGui.CollapsingHeader("Wishlist & Blacklist"))
                return;

            var filterData = LootFilter.FilterData;

            // Item search input
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputTextWithHint("##ItemSearch", "Search items to add...", ref _itemSearchText, 128))
                _searchDirty = true;

            // Perform search
            if (_searchDirty && _itemSearchText.Length >= 2)
            {
                _searchDirty = false;
                _searchResults.Clear();
                var allItems = EftDataManager.AllItems;
                int count = 0;
                foreach (var kvp in allItems)
                {
                    if (count >= MaxSearchResults)
                        break;
                    var item = kvp.Value;
                    if (item.ShortName.Contains(_itemSearchText, StringComparison.OrdinalIgnoreCase) ||
                        item.Name.Contains(_itemSearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        _searchResults.Add(item);
                        count++;
                    }
                }
            }
            else if (_itemSearchText.Length < 2)
            {
                _searchResults.Clear();
            }

            // Search results with add buttons
            if (_searchResults.Count > 0)
            {
                ImGui.BeginChild("##ItemSearchResults", new Vector2(-1, Math.Min(_searchResults.Count * 24 + 4, 200)), ImGuiChildFlags.Borders);
                for (int i = 0; i < _searchResults.Count; i++)
                {
                    var item = _searchResults[i];
                    bool isWL = filterData.IsWishlisted(item.BsgId);
                    bool isBL = filterData.IsBlacklisted(item.BsgId);

                    // Wishlist button
                    if (isWL)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0.6f, 0.65f, 1f));
                        if (ImGui.SmallButton($"\u2605##{i}"))
                        {
                            filterData.RemoveFromWishlist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.SmallButton($"+W##{i}"))
                        {
                            filterData.AddToWishlist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(isWL ? "Remove from wishlist" : "Add to wishlist");

                    ImGui.SameLine();

                    // Blacklist button
                    if (isBL)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.15f, 0.15f, 1f));
                        if (ImGui.SmallButton($"\u2717##{i}"))
                        {
                            filterData.RemoveFromBlacklist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.SmallButton($"+B##{i}"))
                        {
                            filterData.AddToBlacklist(item.BsgId);
                            LootFilter.SaveFilterData();
                        }
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip(isBL ? "Remove from blacklist" : "Add to blacklist");

                    ImGui.SameLine();
                    string priceStr = item.BestPrice > 0 ? $" ({LootFilter.FormatPrice(item.BestPrice)})" : "";
                    ImGui.Text($"{item.ShortName}{priceStr}");
                    if (ImGui.IsItemHovered() && item.Name != item.ShortName)
                        ImGui.SetTooltip(item.Name);
                }
                ImGui.EndChild();
            }

            ImGui.Spacing();

            // Current wishlist
            if (filterData.Wishlist.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0.9f, 1f, 1f));
                ImGui.Text($"\u2605 Wishlist ({filterData.Wishlist.Count})");
                ImGui.PopStyleColor();

                string? removeWL = null;
                for (int i = 0; i < filterData.Wishlist.Count; i++)
                {
                    var bsgId = filterData.Wishlist[i];
                    string name = ResolveItemName(bsgId);
                    if (ImGui.SmallButton($"\u2212##wl{i}"))
                        removeWL = bsgId;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remove from wishlist");
                    ImGui.SameLine();
                    ImGui.Text(name);
                }
                if (removeWL is not null)
                {
                    filterData.RemoveFromWishlist(removeWL);
                    LootFilter.SaveFilterData();
                }
            }

            // Current blacklist
            if (filterData.Blacklist.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.Text($"\u2717 Blacklist ({filterData.Blacklist.Count})");
                ImGui.PopStyleColor();

                string? removeBL = null;
                for (int i = 0; i < filterData.Blacklist.Count; i++)
                {
                    var bsgId = filterData.Blacklist[i];
                    string name = ResolveItemName(bsgId);
                    if (ImGui.SmallButton($"\u2212##bl{i}"))
                        removeBL = bsgId;
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Remove from blacklist");
                    ImGui.SameLine();
                    ImGui.Text(name);
                }
                if (removeBL is not null)
                {
                    filterData.RemoveFromBlacklist(removeBL);
                    LootFilter.SaveFilterData();
                }
            }

            if (filterData.Wishlist.Count == 0 && filterData.Blacklist.Count == 0)
            {
                ImGui.TextDisabled("No wishlisted or blacklisted items.\nSearch above to add items.");
            }
        }

        /// <summary>Resolve a BSG ID to a display name.</summary>
        private static string ResolveItemName(string bsgId)
        {
            if (EftDataManager.AllItems.TryGetValue(bsgId, out var item))
                return item.ShortName;
            return bsgId;
        }

        // ── Options ──────────────────────────────────────────────────────────

        private static void DrawOptionsSection()
        {
            if (!ImGui.CollapsingHeader("Options", ImGuiTreeNodeFlags.DefaultOpen))
                return;

            // Price source
            int priceSource = Config.LootPriceSource;
            ImGui.SetNextItemWidth(140);
            if (ImGui.Combo("Price Source", ref priceSource, LootFilter.PriceSourceLabels, LootFilter.PriceSourceLabels.Length))
                Config.LootPriceSource = priceSource;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Which price to use for filtering and display:\n" +
                    "\u2022 Best \u2014 highest of flea and trader (default)\n" +
                    "\u2022 Flea Market \u2014 flea price (falls back to trader)\n" +
                    "\u2022 Trader \u2014 trader price (falls back to flea)");

            // Price per slot
            bool pps = Config.LootPricePerSlot;
            if (ImGui.Checkbox("Price Per Slot", ref pps))
                Config.LootPricePerSlot = pps;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Divide price by item grid size\nUseful for finding high-value-per-slot items");

            // Show corpses
            bool showCorpses = Config.ShowCorpses;
            if (ImGui.Checkbox("Show Corpses", ref showCorpses))
                Config.ShowCorpses = showCorpses;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show corpse X markers on the radar");
        }

        // ── Footer ───────────────────────────────────────────────────────────

        private static void DrawFooter()
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("\u21ba Reset Defaults"))
                LootFilter.ResetAll();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reset all loot filter settings to defaults");

            ImGui.SameLine();

            if (ImGui.Button("\u2713 Save Config"))
            {
                Config.Save();
                LootFilter.SaveFilterData();
                RadarWindow.NotifyConfigSaved();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save current settings to disk");
        }
    }
}
