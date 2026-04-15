using System.Numerics;
using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.Tarkov.Hideout;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Hideout Stash Panel — stash item table with grouping/sorting/search,
    /// price totals, area upgrade progress, and upgrade requirements display.
    /// </summary>
    internal static class HideoutPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Whether the hideout panel is open.</summary>
        public static bool IsOpen { get; set; }

        private static string _statusText = "Press Refresh to scan hideout...";
        private static string _searchText = string.Empty;
        private static bool _grouped;
        private static int _sortColumn = -1;
        private static bool _sortAscending = true;
        private static bool _showUpgrades = true;
        private static bool _showStash = true;
        private static bool _refreshing;

        // ── Cached upgrade section data ──────────────────────────────────────
        private static IReadOnlyList<HideoutAreaInfo>? _cachedAreaSource;
        private static List<HideoutAreaInfo>? _cachedSortedAreas;
        private static int _cachedReady, _cachedUpgradeable, _cachedMaxed;
        private static string _cachedAreaSummary = "";

        // ── Cached stash display list ────────────────────────────────────────
        private static IReadOnlyList<StashItem>? _cachedStashSource;
        private static bool _cachedGrouped;
        private static string _cachedSearchText = "";
        private static int _cachedStashSortColumn = -1;
        private static bool _cachedStashSortAsc = true;
        private static List<StashItem>? _cachedDisplayList;
        private static string _cachedStashSummary = "";
        private static long _cachedStashTotalBest = -1;

        // ── Colours ──────────────────────────────────────────────────────────
        private static readonly Vector4 ColGreen  = new(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Vector4 ColOrange = new(1.00f, 0.60f, 0.00f, 1f);
        private static readonly Vector4 ColRed    = new(0.94f, 0.33f, 0.31f, 1f);
        private static readonly Vector4 ColGrey   = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColSlate  = new(0.47f, 0.56f, 0.61f, 1f);
        private static readonly Vector4 ColGold   = new(1.00f, 0.84f, 0.00f, 1f);
        private static readonly Vector4 ColDim    = new(1f, 1f, 1f, 0.38f);

        /// <summary>Shared HideoutManager instance from Memory.</summary>
        private static HideoutManager Manager => Memory.Hideout;

        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(600, 650), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("\U0001f3e0 Hideout", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            DrawToolbar();
            ImGui.Separator();

            if (_showStash)
                DrawStashSection();

            if (_showUpgrades)
                DrawUpgradesSection();

            ImGui.End();
        }

        // ── Toolbar ──────────────────────────────────────────────────────────

        private static void DrawToolbar()
        {
            // Enabled / Auto-refresh toggles
            bool hideoutEnabled = Config.HideoutEnabled;
            if (ImGui.Checkbox("Enabled", ref hideoutEnabled))
                Config.HideoutEnabled = hideoutEnabled;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enable hideout stash/area reading");

            ImGui.SameLine();
            if (!Config.HideoutEnabled)
                ImGui.BeginDisabled();

            bool autoRefresh = Config.HideoutAutoRefresh;
            if (ImGui.Checkbox("Auto Refresh", ref autoRefresh))
                Config.HideoutAutoRefresh = autoRefresh;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically refresh on hideout entry");

            if (!Config.HideoutEnabled)
                ImGui.EndDisabled();

            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();

            // Refresh button — allowed in hideout or main menu, not in actual raids
            bool canRefresh = !_refreshing && !Memory.InRaid && Config.HideoutEnabled;
            if (!canRefresh)
                ImGui.BeginDisabled();

            if (ImGui.Button(_refreshing ? "Refreshing..." : "\u21bb Refresh"))
            {
                _refreshing = true;
                Task.Run(() =>
                {
                    try { _statusText = Manager.RefreshAll(); }
                    catch (Exception ex) { _statusText = $"Error: {ex.Message}"; }
                    finally { _refreshing = false; }
                });
            }

            if (!canRefresh)
                ImGui.EndDisabled();

            ImGui.SameLine();
            if (Memory.InHideout)
                ImGui.TextColored(ColGreen, _statusText);
            else
                ImGui.TextColored(ColSlate, _statusText);

            // Section toggles
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 200);
            ImGui.Checkbox("Stash", ref _showStash);
            ImGui.SameLine();
            ImGui.Checkbox("Upgrades", ref _showUpgrades);
            ImGui.SameLine();
            ImGui.Checkbox("Group", ref _grouped);
        }

        // ── Stash Section ────────────────────────────────────────────────────

        private static void DrawStashSection()
        {
            var mgr = Manager;
            var items = mgr.Items;
            if (items.Count == 0)
            {
                ImGui.TextDisabled("No stash data. Press Refresh while in hideout.");
                return;
            }

            // Cache summary string — only rebuild when totals change
            long totalBest = mgr.TotalBestValue;
            if (!ReferenceEquals(items, _cachedStashSource) || totalBest != _cachedStashTotalBest)
            {
                _cachedStashTotalBest = totalBest;
                _cachedStashSummary =
                    $"Stash: {items.Count} items  |  Best: {HideoutManager.FormatPrice(totalBest)}  |  " +
                    $"Trader: {HideoutManager.FormatPrice(mgr.TotalTraderValue)}  |  Flea: {HideoutManager.FormatPrice(mgr.TotalFleaValue)}";
            }

            ImGui.TextColored(ColGold, _cachedStashSummary);

            // Search
            ImGui.SetNextItemWidth(250);
            ImGui.InputTextWithHint("##stashSearch", "Search items...", ref _searchText, 128);
            ImGui.Spacing();

            // Rebuild display list only when inputs change
            bool needsRebuild = !ReferenceEquals(items, _cachedStashSource)
                || _grouped != _cachedGrouped
                || !string.Equals(_searchText, _cachedSearchText, StringComparison.Ordinal);

            if (needsRebuild)
            {
                _cachedStashSource = items;
                _cachedGrouped = _grouped;
                _cachedSearchText = _searchText;
                _cachedDisplayList = BuildDisplayList(items);
                // Force re-sort with current settings
                if (_sortColumn >= 0)
                    SortDisplayList(_cachedDisplayList);
                _cachedStashSortColumn = _sortColumn;
                _cachedStashSortAsc = _sortAscending;
            }

            var displayItems = _cachedDisplayList!;

            // Table
            var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable
                      | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

            float availH = ImGui.GetContentRegionAvail().Y;
            float tableH = _showUpgrades ? Math.Max(200, availH * 0.45f) : availH;

            if (ImGui.BeginTable("StashTable", 6, flags, new Vector2(0, tableH)))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort, 3f);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.None, 0.5f);
                ImGui.TableSetupColumn("Trader", ImGuiTableColumnFlags.None, 1.2f);
                ImGui.TableSetupColumn("Flea", ImGuiTableColumnFlags.None, 1.2f);
                ImGui.TableSetupColumn("Best", ImGuiTableColumnFlags.DefaultSort, 1.2f);
                ImGui.TableSetupColumn("Sell On", ImGuiTableColumnFlags.None, 0.8f);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                // Handle sorting — only re-sort when sort specs change
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    var spec = sortSpecs.Specs;
                    _sortColumn = spec.ColumnIndex;
                    _sortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                    sortSpecs.SpecsDirty = false;
                }

                if (_sortColumn >= 0 && (_sortColumn != _cachedStashSortColumn || _sortAscending != _cachedStashSortAsc))
                {
                    _cachedStashSortColumn = _sortColumn;
                    _cachedStashSortAsc = _sortAscending;
                    SortDisplayList(displayItems);
                }

                for (int i = 0; i < displayItems.Count; i++)
                {
                    var item = displayItems[i];
                    bool isNeeded = mgr.NeededItemIds.Contains(item.Id);

                    ImGui.TableNextRow();

                    // Name (highlight if needed for upgrade)
                    ImGui.TableNextColumn();
                    if (isNeeded)
                    {
                        ImGui.TextColored(ColGold, item.Name);
                        if (ImGui.IsItemHovered() && mgr.NeededItemCounts.TryGetValue(item.Id, out int needed))
                            ImGui.SetTooltip($"Needed for hideout upgrade: {needed} more");
                    }
                    else
                    {
                        ImGui.TextUnformatted(item.Name);
                    }

                    // Qty
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.StackCount.ToString());

                    // Trader
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(HideoutManager.FormatPrice(item.TraderPrice * item.StackCount));

                    // Flea
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(HideoutManager.FormatPrice(item.FleaPrice * item.StackCount));

                    // Best
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ColGreen, HideoutManager.FormatPrice(item.BestPrice));

                    // Sell On
                    ImGui.TableNextColumn();
                    if (item.SellOnFlea)
                        ImGui.TextColored(ColOrange, "Flea");
                    else
                        ImGui.TextUnformatted("Trader");
                }

                ImGui.EndTable();
            }
        }

        // ── Upgrades Section ─────────────────────────────────────────────────

        private static void DrawUpgradesSection()
        {
            var mgr = Manager;
            var areas = mgr.Areas;
            if (areas.Count == 0)
            {
                ImGui.TextDisabled("No area data. Press Refresh while in hideout.");
                return;
            }

            ImGui.Spacing();
            ImGui.Separator();

            // Rebuild sorted list + summary only when the underlying data changes
            if (!ReferenceEquals(areas, _cachedAreaSource))
            {
                _cachedAreaSource = areas;

                int ready = 0, upgradeable = 0, maxed = 0;
                for (int i = 0; i < areas.Count; i++)
                {
                    if (areas[i].IsMaxLevel) maxed++;
                    else
                    {
                        upgradeable++;
                        if (areas[i].Status is EAreaStatus.ReadyToUpgrade or EAreaStatus.ReadyToConstruct)
                            ready++;
                    }
                }
                _cachedReady = ready;
                _cachedUpgradeable = upgradeable;
                _cachedMaxed = maxed;
                _cachedAreaSummary = $"Areas: {ready} ready  \u00b7  {upgradeable} upgradeable  \u00b7  {maxed} maxed";

                if (_cachedSortedAreas is null)
                    _cachedSortedAreas = new List<HideoutAreaInfo>(areas);
                else
                {
                    _cachedSortedAreas.Clear();
                    for (int i = 0; i < areas.Count; i++)
                        _cachedSortedAreas.Add(areas[i]);
                }
                _cachedSortedAreas.Sort(static (a, b) =>
                {
                    int ma = a.IsMaxLevel ? 1 : 0;
                    int mb = b.IsMaxLevel ? 1 : 0;
                    if (ma != mb) return ma.CompareTo(mb);
                    int pa = HideoutManager.GetStatusPriority(a.Status);
                    int pb = HideoutManager.GetStatusPriority(b.Status);
                    if (pa != pb) return pa.CompareTo(pb);
                    return ((int)a.AreaType).CompareTo((int)b.AreaType);
                });
            }

            ImGui.TextColored(ColGold, _cachedAreaSummary);
            ImGui.Spacing();

            for (int i = 0; i < _cachedSortedAreas!.Count; i++)
            {
                var area = _cachedSortedAreas[i];
                DrawAreaCard(area);
            }
        }

        private static void DrawAreaCard(HideoutAreaInfo area)
        {
            var statusColor = GetStatusColor(area.Status);
            string name = HideoutManager.FormatAreaName(area.AreaType.ToString());
            string statusLabel = HideoutManager.FormatStatus(area.Status);
            string levelLabel = area.IsMaxLevel
                ? $"lv{area.CurrentLevel}"
                : $"lv{area.CurrentLevel} → {area.CurrentLevel + 1}";

            // Dim maxed areas
            if (area.IsMaxLevel)
                ImGui.PushStyleColor(ImGuiCol.Text, ColDim);

            // Area header: name + level + status
            bool expanded = !area.IsMaxLevel && area.NextLevelRequirements.Count > 0;
            string headerId = $"{name}  {levelLabel}##area_{area.AreaType}";

            if (expanded)
            {
                if (ImGui.TreeNodeEx(headerId, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Status badge
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                    ImGui.TextColored(statusColor, statusLabel);

                    DrawRequirements(area.NextLevelRequirements);
                    ImGui.TreePop();
                }
                else
                {
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                    ImGui.TextColored(statusColor, statusLabel);
                }
            }
            else
            {
                ImGui.TextUnformatted($"  {name}  {levelLabel}");
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(statusLabel).X - 8);
                ImGui.TextColored(statusColor, statusLabel);
            }

            if (area.IsMaxLevel)
                ImGui.PopStyleColor();
        }

        private static void DrawRequirements(IReadOnlyList<HideoutRequirement> reqs)
        {
            for (int i = 0; i < reqs.Count; i++)
            {
                var req = reqs[i];
                var icon = req.Fulfilled ? "✓" : "✗";
                var color = req.Fulfilled ? ColGreen : ColRed;
                var desc = FormatRequirement(req);

                ImGui.TextColored(color, $"    {icon}");
                ImGui.SameLine();
                ImGui.TextUnformatted(desc);
            }
        }

        private static string FormatRequirement(HideoutRequirement req) => req.Type switch
        {
            ERequirementType.Item or ERequirementType.Tool when !req.Fulfilled
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}  ({req.CurrentCount}/{req.RequiredCount})",
            ERequirementType.Item or ERequirementType.Tool
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}",
            ERequirementType.Area
                => $"{HideoutManager.FormatAreaName(req.RequiredArea.ToString())} lv{req.RequiredLevel}",
            ERequirementType.Skill
                => $"{req.SkillName ?? "Skill"} lv{req.SkillLevel}",
            ERequirementType.TraderLoyalty
                => $"{req.TraderId ?? "Trader"} LL{req.LoyaltyLevel}",
            ERequirementType.TraderUnlock => "Trader unlock",
            ERequirementType.QuestComplete => "Quest complete",
            _ => req.Type.ToString()
        };

        // ── Display list helpers ─────────────────────────────────────────────

        private static List<StashItem> BuildDisplayList(IReadOnlyList<StashItem> items)
        {
            List<StashItem> result;

            if (_grouped)
            {
                var groups = new Dictionary<string, (StashItem First, int TotalQty)>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (groups.TryGetValue(item.Id, out var existing))
                        groups[item.Id] = (existing.First, existing.TotalQty + item.StackCount);
                    else
                        groups[item.Id] = (item, item.StackCount);
                }

                result = new List<StashItem>(groups.Count);
                foreach (var (_, (first, totalQty)) in groups)
                {
                    result.Add(new StashItem(
                        Id: first.Id,
                        Name: first.Name,
                        TraderPrice: first.TraderPrice,
                        FleaPrice: first.FleaPrice,
                        StackCount: totalQty));
                }
            }
            else
            {
                result = new List<StashItem>(items.Count);
                for (int i = 0; i < items.Count; i++)
                    result.Add(items[i]);
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var search = _searchText.Trim();
                result.RemoveAll(i =>
                    !i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    && !i.Id.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return result;
        }

        private static void SortDisplayList(List<StashItem> items)
        {
            items.Sort(_sortColumn switch
            {
                0 => _sortAscending
                    ? static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)
                    : static (a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase),
                1 => _sortAscending
                    ? static (a, b) => a.StackCount.CompareTo(b.StackCount)
                    : static (a, b) => b.StackCount.CompareTo(a.StackCount),
                2 => _sortAscending
                    ? static (a, b) => (a.TraderPrice * a.StackCount).CompareTo(b.TraderPrice * b.StackCount)
                    : static (a, b) => (b.TraderPrice * b.StackCount).CompareTo(a.TraderPrice * a.StackCount),
                3 => _sortAscending
                    ? static (a, b) => (a.FleaPrice * a.StackCount).CompareTo(b.FleaPrice * b.StackCount)
                    : static (a, b) => (b.FleaPrice * b.StackCount).CompareTo(a.FleaPrice * a.StackCount),
                4 => _sortAscending
                    ? static (a, b) => a.BestPrice.CompareTo(b.BestPrice)
                    : static (a, b) => b.BestPrice.CompareTo(a.BestPrice),
                5 => _sortAscending
                    ? static (a, b) => a.SellOnFlea.CompareTo(b.SellOnFlea)
                    : static (a, b) => b.SellOnFlea.CompareTo(a.SellOnFlea),
                _ => static (_, _) => 0
            });
        }

        private static Vector4 GetStatusColor(EAreaStatus s) => s switch
        {
            EAreaStatus.ReadyToConstruct or EAreaStatus.ReadyToUpgrade or
            EAreaStatus.ReadyToInstallConstruct or EAreaStatus.ReadyToInstallUpgrade => ColGreen,
            EAreaStatus.Constructing or EAreaStatus.Upgrading or
            EAreaStatus.AutoUpgrading => ColOrange,
            EAreaStatus.NoFutureUpgrades => ColSlate,
            _ => ColGrey
        };
    }
}
