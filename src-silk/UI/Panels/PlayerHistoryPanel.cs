using System.Diagnostics;
using System.Numerics;
using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Player History Panel — displays all human players seen across raids.
    /// Supports sorting, search, remove, clear, add-to-watchlist, and open profile.
    /// </summary>
    internal static class PlayerHistoryPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Whether the panel is open.</summary>
        public static bool IsOpen { get; set; }

        private static string _searchText = string.Empty;
        private static int _sortColumn = -1;
        private static bool _sortAscending = true;

        // Cached display list
        private static IReadOnlyList<PlayerHistoryEntry>? _cachedSource;
        private static string _cachedSearch = "";
        private static int _cachedSortCol = -1;
        private static bool _cachedSortAsc = true;
        private static List<PlayerHistoryEntry>? _cachedDisplay;

        // Colours
        private static readonly Vector4 ColGreen = new(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Vector4 ColRed = new(0.94f, 0.33f, 0.31f, 1f);
        private static readonly Vector4 ColGrey = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColGold = new(1.00f, 0.84f, 0.00f, 1f);

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2630 Player History", ref isOpen, new Vector2(620, 450));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            var history = Memory.PlayerHistory;
            if (history is null)
            {
                ImGui.TextColored(ColGrey, "Player history not available.");
                return;
            }

            DrawToolbar(history);
            ImGui.Separator();
            DrawTable(history);
        }

        private static void DrawToolbar(PlayerHistory history)
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##histSearch", "Search...", ref _searchText, 128);
            ImGui.SameLine();
            ImGui.TextColored(ColGrey, $"{history.Count} entries");
            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                history.Clear();
                InvalidateCache();
            }
        }

        private static void DrawTable(PlayerHistory history)
        {
            var entries = history.Entries;
            var display = GetDisplayList(entries);

            if (display.Count == 0)
            {
                ImGui.TextColored(ColGrey, "No players in history.");
                return;
            }

            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Sortable |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingFixedFit;

            if (!ImGui.BeginTable("##historyTable", 5, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.DefaultSort, 150);
            ImGui.TableSetupColumn("Account ID", ImGuiTableColumnFlags.None, 120);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.None, 80);
            ImGui.TableSetupColumn("Last Seen", ImGuiTableColumnFlags.None, 100);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort, 120);
            ImGui.TableHeadersRow();

            // Handle sort
            var sortSpecs = ImGui.TableGetSortSpecs();
            if (sortSpecs.SpecsDirty)
            {
                if (sortSpecs.SpecsCount > 0)
                {
                    unsafe
                    {
                        var specs = sortSpecs.Specs;
                        _sortColumn = specs.ColumnIndex;
                        _sortAscending = specs.SortDirection == ImGuiSortDirection.Ascending;
                    }
                }
                sortSpecs.SpecsDirty = false;
                InvalidateCache();
                display = GetDisplayList(entries);
            }

            for (int i = 0; i < display.Count; i++)
            {
                var entry = display[i];
                ImGui.TableNextRow();

                // Name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Name);

                // Account ID
                ImGui.TableNextColumn();
                if (!string.IsNullOrEmpty(entry.AccountId))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ColGold);
                    if (ImGui.Selectable(entry.AccountId + "##prof" + i))
                        OpenPlayerProfile(entry.AccountId);
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Click to open tarkov.dev profile");
                }
                else
                {
                    ImGui.TextColored(ColGrey, "--");
                }

                // Type
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.TypeLabel);

                // Last Seen
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.LastSeenFormatted);

                // Actions
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Watch##" + i))
                    AddToWatchlist(entry);
                ImGui.SameLine();
                if (ImGui.SmallButton("X##" + i))
                {
                    Memory.PlayerHistory?.Remove(entry);
                    InvalidateCache();
                }
            }

            ImGui.EndTable();
        }

        private static List<PlayerHistoryEntry> GetDisplayList(IReadOnlyList<PlayerHistoryEntry> source)
        {
            if (_cachedDisplay is not null &&
                ReferenceEquals(_cachedSource, source) &&
                _cachedSearch == _searchText &&
                _cachedSortCol == _sortColumn &&
                _cachedSortAsc == _sortAscending)
                return _cachedDisplay;

            _cachedSource = source;
            _cachedSearch = _searchText;
            _cachedSortCol = _sortColumn;
            _cachedSortAsc = _sortAscending;

            var list = new List<PlayerHistoryEntry>(source.Count);
            var search = _searchText.AsSpan();

            for (int i = 0; i < source.Count; i++)
            {
                var e = source[i];
                if (search.Length > 0 &&
                    !e.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !e.AccountId.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !e.TypeLabel.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(e);
            }

            if (_sortColumn >= 0)
            {
                list.Sort(static (a, b) =>
                {
                    int cmp = _sortColumn switch
                    {
                        0 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                        1 => string.Compare(a.AccountId, b.AccountId, StringComparison.OrdinalIgnoreCase),
                        2 => string.Compare(a.TypeLabel, b.TypeLabel, StringComparison.OrdinalIgnoreCase),
                        3 => a.LastSeen.CompareTo(b.LastSeen),
                        _ => 0
                    };
                    return _sortAscending ? cmp : -cmp;
                });
            }

            _cachedDisplay = list;
            return list;
        }

        private static void InvalidateCache()
        {
            _cachedDisplay = null;
            _cachedSource = null;
        }

        private static void AddToWatchlist(PlayerHistoryEntry entry)
        {
            if (string.IsNullOrEmpty(entry.AccountId))
            {
                Log.WriteLine("[PlayerHistory] Cannot add to watchlist — no Account ID resolved yet.");
                return;
            }

            var watchlist = Memory.PlayerWatchlist;
            if (watchlist is null)
                return;

            watchlist.Add(new PlayerWatchlistEntry
            {
                AccountId = entry.AccountId,
                Name = entry.Name,
                Reason = "Player History",
                Tag = "HIST"
            });

            Log.WriteLine($"[PlayerHistory] Added '{entry.Name}' to watchlist.");
        }

        private static void OpenPlayerProfile(string accountId)
        {
            try
            {
                var url = $"https://tarkov.dev/players/regular/{accountId}";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[PlayerHistory] Error opening profile: {ex.Message}");
            }
        }
    }
}
