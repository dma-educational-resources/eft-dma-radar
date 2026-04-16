using System.Diagnostics;
using System.Numerics;
using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Player Watchlist Panel — manage manually-tracked players.
    /// Supports add, remove, clear, search, and open profile.
    /// </summary>
    internal static class PlayerWatchlistPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Whether the panel is open.</summary>
        public static bool IsOpen { get; set; }

        private static string _searchText = string.Empty;
        private static string _addAccountId = string.Empty;
        private static string _addName = string.Empty;
        private static string _addReason = string.Empty;
        private static string _addTag = string.Empty;

        // Cached display list
        private static IReadOnlyDictionary<string, PlayerWatchlistEntry>? _cachedSource;
        private static string _cachedSearch = "";
        private static List<PlayerWatchlistEntry>? _cachedDisplay;

        // Colours
        private static readonly Vector4 ColGreen = new(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Vector4 ColRed = new(0.94f, 0.33f, 0.31f, 1f);
        private static readonly Vector4 ColGrey = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColGold = new(1.00f, 0.84f, 0.00f, 1f);
        private static readonly Vector4 ColCyan = new(0.00f, 0.80f, 0.80f, 1f);

        public static void Draw()
        {
            bool isOpen = IsOpen;
            ImGui.SetNextWindowSize(new Vector2(600, 420), ImGuiCond.FirstUseEver);
            if (!ImGui.Begin("\U0001f50d Player Watchlist", ref isOpen, ImGuiWindowFlags.NoCollapse))
            {
                IsOpen = isOpen;
                ImGui.End();
                return;
            }
            IsOpen = isOpen;

            var watchlist = Memory.PlayerWatchlist;
            if (watchlist is null)
            {
                ImGui.TextColored(ColGrey, "Watchlist not available.");
                ImGui.End();
                return;
            }

            DrawAddSection(watchlist);
            ImGui.Separator();
            DrawToolbar(watchlist);
            ImGui.Separator();
            DrawTable(watchlist);

            ImGui.End();
        }

        private static void DrawAddSection(PlayerWatchlist watchlist)
        {
            if (ImGui.CollapsingHeader("Add Player", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.SetNextItemWidth(160);
                ImGui.InputTextWithHint("##wlAccId", "Account ID *", ref _addAccountId, 64);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                ImGui.InputTextWithHint("##wlName", "Name", ref _addName, 64);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.InputTextWithHint("##wlReason", "Reason", ref _addReason, 64);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                ImGui.InputTextWithHint("##wlTag", "Tag", ref _addTag, 16);
                ImGui.SameLine();

                bool canAdd = !string.IsNullOrWhiteSpace(_addAccountId);
                if (!canAdd) ImGui.BeginDisabled();
                if (ImGui.Button("Add"))
                {
                    watchlist.Add(new PlayerWatchlistEntry
                    {
                        AccountId = _addAccountId.Trim(),
                        Name = string.IsNullOrWhiteSpace(_addName) ? "Unknown" : _addName.Trim(),
                        Reason = _addReason.Trim(),
                        Tag = _addTag.Trim()
                    });
                    _addAccountId = string.Empty;
                    _addName = string.Empty;
                    _addReason = string.Empty;
                    _addTag = string.Empty;
                    InvalidateCache();
                }
                if (!canAdd) ImGui.EndDisabled();
            }
        }

        private static void DrawToolbar(PlayerWatchlist watchlist)
        {
            ImGui.SetNextItemWidth(200);
            ImGui.InputTextWithHint("##wlSearch", "Search...", ref _searchText, 128);
            ImGui.SameLine();
            ImGui.TextColored(ColGrey, $"{watchlist.Count} entries");
            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                watchlist.Clear();
                InvalidateCache();
            }
        }

        private static void DrawTable(PlayerWatchlist watchlist)
        {
            var entries = watchlist.Entries;
            var display = GetDisplayList(entries);

            if (display.Count == 0)
            {
                ImGui.TextColored(ColGrey, "Watchlist is empty.");
                return;
            }

            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingFixedFit;

            if (!ImGui.BeginTable("##watchlistTable", 6, flags, new Vector2(0, ImGui.GetContentRegionAvail().Y)))
                return;

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 120);
            ImGui.TableSetupColumn("Account ID", ImGuiTableColumnFlags.None, 120);
            ImGui.TableSetupColumn("Tag", ImGuiTableColumnFlags.None, 50);
            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 100);
            ImGui.TableSetupColumn("Added", ImGuiTableColumnFlags.None, 90);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.NoSort, 80);
            ImGui.TableHeadersRow();

            for (int i = 0; i < display.Count; i++)
            {
                var entry = display[i];
                ImGui.TableNextRow();

                // Name
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Name);

                // Account ID
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, ColGold);
                if (ImGui.Selectable(entry.AccountId + "##wlprof" + i))
                    OpenPlayerProfile(entry.AccountId);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Click to open tarkov.dev profile");

                // Tag
                ImGui.TableNextColumn();
                if (!string.IsNullOrEmpty(entry.Tag))
                    ImGui.TextColored(ColCyan, entry.Tag);
                else
                    ImGui.TextColored(ColGrey, "--");

                // Reason
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.Reason);

                // Added
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.AddedDate.ToString("MM/dd/yy"));

                // Actions
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("X##wl" + i))
                {
                    watchlist.Remove(entry.AccountId);
                    InvalidateCache();
                }
            }

            ImGui.EndTable();
        }

        private static List<PlayerWatchlistEntry> GetDisplayList(IReadOnlyDictionary<string, PlayerWatchlistEntry> source)
        {
            if (_cachedDisplay is not null &&
                ReferenceEquals(_cachedSource, source) &&
                _cachedSearch == _searchText)
                return _cachedDisplay;

            _cachedSource = source;
            _cachedSearch = _searchText;

            var list = new List<PlayerWatchlistEntry>(source.Count);
            foreach (var entry in source.Values)
            {
                if (_searchText.Length > 0 &&
                    !entry.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !entry.AccountId.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Reason.Contains(_searchText, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Tag.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(entry);
            }

            // Sort by name
            list.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            _cachedDisplay = list;
            return list;
        }

        private static void InvalidateCache()
        {
            _cachedDisplay = null;
            _cachedSource = null;
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
                Log.WriteLine($"[PlayerWatchlist] Error opening profile: {ex.Message}");
            }
        }
    }
}
