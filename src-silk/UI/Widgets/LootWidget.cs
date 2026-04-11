using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Widgets
{
    /// <summary>
    /// ImGui loot table widget — shows nearby visible loot grouped by item,
    /// sortable by name, price, quantity, total value, or distance.
    /// </summary>
    internal static class LootWidget
    {
        private const int MAX_ROWS = 50;

        /// <summary>Whether the loot widget is open.</summary>
        public static bool IsOpen { get; set; }

        // Reusable per-frame collections
        private static readonly Dictionary<string, LootGroup> _groups = new(128);
        private static readonly List<LootGroup> _sorted = new(128);

        // Cached sort state — persists across frames while data is rebuilt
        private static uint _sortColumnId = 1; // Default: Price
        private static ImGuiSortDirection _sortDirection = ImGuiSortDirection.Descending;

        /// <summary>Draw the loot widget.</summary>
        public static void Draw()
        {
            var localPlayer = Memory.LocalPlayer;
            var loot = Memory.Loot;
            if (localPlayer is null)
                return;

            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(480, 360), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(360, 180), new Vector2(700, 800));

            if (!ImGui.Begin("Loot", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            // Build grouped snapshot
            _groups.Clear();
            _sorted.Clear();
            long totalValue = 0;
            int visibleCount = 0;

            if (loot is not null)
            {
                var localPos = localPlayer.Position;

                for (int i = 0; i < loot.Count; i++)
                {
                    var item = loot[i];
                    if (!item.ShouldDraw())
                        continue;

                    int price = item.DisplayPrice;
                    float dist = Vector3.Distance(localPos, item.Position);
                    bool important = LootFilter.IsImportant(price);
                    visibleCount++;
                    totalValue += price;

                    // Group by ShortName — keep closest distance and check importance
                    if (_groups.TryGetValue(item.ShortName, out var group))
                    {
                        group.Quantity++;
                        group.TotalValue += price;
                        if (dist < group.NearestDist)
                            group.NearestDist = dist;
                        group.IsImportant |= important;
                    }
                    else
                    {
                        var g = new LootGroup
                        {
                            ShortName = item.ShortName,
                            FullName = item.Name,
                            PricePerItem = price,
                            TotalValue = price,
                            Quantity = 1,
                            NearestDist = dist,
                            IsImportant = important,
                        };
                        _groups[item.ShortName] = g;
                        _sorted.Add(g);
                    }
                }
            }

            // Summary header
            DrawSummary(visibleCount, totalValue, loot?.Count ?? 0);
            ImGui.Separator();

            if (visibleCount == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No loot matches current filters");
                ImGui.End();
                return;
            }

            // Table
            DrawTable();

            ImGui.End();
        }

        private static void DrawSummary(int visible, long totalValue, int total)
        {
            // Left: value
            if (totalValue > 0)
            {
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), LootFilter.FormatPrice((int)totalValue));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "total value");
                ImGui.SameLine();
            }

            // Right-aligned: count
            string countText = total > 0 ? $"{visible}/{total}" : "0";
            float textWidth = ImGui.CalcTextSize(countText).X + ImGui.CalcTextSize(" items").X;
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - textWidth - ImGui.GetStyle().WindowPadding.X);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), countText);
            ImGui.SameLine(0, 0);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), " items");
        }

        private static void DrawTable()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 2));

            var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                        ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY |
                        ImGuiTableFlags.Sortable | ImGuiTableFlags.SortMulti |
                        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;

            if (!ImGui.BeginTable("LootTable", 5, flags))
            {
                ImGui.PopStyleVar();
                return;
            }

            ImGui.TableSetupColumn("Item",  ImGuiTableColumnFlags.WidthStretch, 0f, 0);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort | ImGuiTableColumnFlags.PreferSortDescending, 65f, 1);
            ImGui.TableSetupColumn("Qty",   ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, 32f, 2);
            ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.PreferSortDescending, 65f, 3);
            ImGui.TableSetupColumn("Dist",  ImGuiTableColumnFlags.WidthFixed, 42f, 4);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Sort
            ApplySorting();

            // Render rows (capped)
            int rowCount = Math.Min(_sorted.Count, MAX_ROWS);
            for (int i = 0; i < rowCount; i++)
            {
                var g = _sorted[i];
                ImGui.TableNextRow();

                var color = g.IsImportant
                    ? new Vector4(0.2f, 1f, 0.2f, 1f)
                    : new Vector4(0.85f, 0.85f, 0.85f, 1f);

                // Item name
                ImGui.TableNextColumn();
                ImGui.TextColored(color, g.ShortName);
                if (ImGui.IsItemHovered() && g.FullName != g.ShortName)
                    ImGui.SetTooltip(g.FullName);

                // Price per item
                ImGui.TableNextColumn();
                ImGui.TextColored(color, LootFilter.FormatPrice(g.PricePerItem));

                // Quantity
                ImGui.TableNextColumn();
                if (g.Quantity > 1)
                    ImGui.TextColored(color, g.Quantity.ToString());
                else
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 0.7f), "1");

                // Total value
                ImGui.TableNextColumn();
                if (g.Quantity > 1)
                    ImGui.TextColored(color, LootFilter.FormatPrice(g.TotalValue));
                else
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 0.7f), "-");

                // Distance
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"{(int)g.NearestDist}m");
            }

            if (_sorted.Count > MAX_ROWS)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f),
                    $"... and {_sorted.Count - MAX_ROWS} more");
            }

            ImGui.EndTable();
            ImGui.PopStyleVar();
        }

        private static void ApplySorting()
        {
            // Update cached sort state when the user clicks a column header
            var sortSpecs = ImGui.TableGetSortSpecs();
            if (sortSpecs.SpecsDirty && sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                _sortColumnId = spec.ColumnUserID;
                _sortDirection = spec.SortDirection;
                sortSpecs.SpecsDirty = false;
            }

            if (_sorted.Count <= 1)
                return;

            // Always sort — data is rebuilt fresh every frame
            _sorted.Sort(static (a, b) =>
            {
                int cmp = _sortColumnId switch
                {
                    0 => string.Compare(a.ShortName, b.ShortName, StringComparison.OrdinalIgnoreCase),
                    1 => a.PricePerItem.CompareTo(b.PricePerItem),
                    2 => a.Quantity.CompareTo(b.Quantity),
                    3 => a.TotalValue.CompareTo(b.TotalValue),
                    4 => a.NearestDist.CompareTo(b.NearestDist),
                    _ => a.PricePerItem.CompareTo(b.PricePerItem),
                };
                return _sortDirection == ImGuiSortDirection.Ascending ? cmp : -cmp;
            });
        }

        /// <summary>
        /// A group of identical items (same ShortName) with aggregated stats.
        /// </summary>
        private sealed class LootGroup
        {
            public string ShortName = string.Empty;
            public string FullName = string.Empty;
            public int PricePerItem;
            public int TotalValue;
            public int Quantity;
            public float NearestDist;
            public bool IsImportant;
        }
    }
}
