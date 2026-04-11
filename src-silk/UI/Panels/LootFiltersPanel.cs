using ImGuiNET;
using eft_dma_radar.Silk.Config;
using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Loot Filters Panel — search, price thresholds, price mode, quick presets, and live stats.
    /// </summary>
    internal static class LootFiltersPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>
        /// Whether the loot filters panel is open.
        /// </summary>
        public static bool IsOpen { get; set; }

        /// <summary>
        /// Draw the loot filters panel.
        /// </summary>
        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(400, 420), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("\u25a3 Loot Filters", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            DrawStatusBar();
            ImGui.Spacing();
            DrawSearchSection();
            DrawPriceSection();
            DrawOptionsSection();
            DrawFooter();

            ImGui.End();
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
                ImGui.TextWrapped("\u26a0 Min price is above important price — no items will highlight as important.");
                ImGui.PopStyleColor();
            }
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
                RadarWindow.NotifyConfigSaved();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Save current settings to disk");
        }
    }
}
