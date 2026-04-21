using System.Numerics;

using eft_dma_radar.Silk.DMA;
using eft_dma_radar.Silk.Misc.Data;

using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    /// <summary>
    /// Quest Info Panel — shows active quests grouped by trader,
    /// with a map filter combo, collapsible quest entries, objective details,
    /// required keys, required items, and filter toggles.
    /// Works in both lobby (via LobbyQuestReader) and in-raid.
    /// </summary>
    internal static class QuestPanel
    {
        private static SilkConfig Config => SilkProgram.Config;

        /// <summary>Whether the quest panel is open.</summary>
        public static bool IsOpen { get; set; }

        // ── Filter state ─────────────────────────────────────────────────────
        private static bool _showKeys = true;
        private static bool _showRequiredItems = true;
        private static bool _hideCompleted;
        private static int _selectedMapIndex; // 0 = All Maps, 1 = Current Map (in-raid only), 2+ = specific maps

        // ── Collapsed sections ───────────────────────────────────────────────
        private static readonly HashSet<string> _collapsedQuests = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _collapsedTraders = new(StringComparer.OrdinalIgnoreCase);

        // ── Colours ──────────────────────────────────────────────────────────
        private static readonly Vector4 ColGreen    = new(0.30f, 0.69f, 0.31f, 1f);
        private static readonly Vector4 ColOrange   = new(1.00f, 0.60f, 0.00f, 1f);
        private static readonly Vector4 ColGrey     = new(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Vector4 ColCyan     = new(0.00f, 0.80f, 0.80f, 1f);
        private static readonly Vector4 ColYellow   = new(1.00f, 0.84f, 0.00f, 1f);
        private static readonly Vector4 ColDim      = new(1f, 1f, 1f, 0.38f);
        private static readonly Vector4 ColWhite    = new(1f, 1f, 1f, 1f);
        private static readonly Vector4 ColMagenta  = new(0.85f, 0.44f, 0.84f, 1f);
        private static readonly Vector4 ColKappa    = new(0.58f, 0.44f, 0.86f, 1f);
        private static readonly Vector4 ColBlue     = new(0.40f, 0.60f, 1.00f, 1f);

        // ── BSG map ID → display name ────────────────────────────────────────
        private static readonly Dictionary<string, string> _bsgMapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "55f2d3fd4bdc2d5f408b4567", "Factory" },
            { "59fc81d786f774390775787e", "Factory" },
            { "56f40101d2720b2a4d8b45d6", "Customs" },
            { "5704e3c2d2720bac5b8b4567", "Woods" },
            { "5704e4dad2720bb55b8b4567", "Lighthouse" },
            { "5704e554d2720bac5b8b456e", "Shoreline" },
            { "6733700029c367a3d40b02af", "Labyrinth" },
            { "5704e5fad2720bc05b8b4567", "Reserve" },
            { "5714dbc024597771384a510d", "Interchange" },
            { "5714dc692459777137212e12", "Streets" },
            { "5b0fc42d86f7744a585f9105", "Labs" },
            { "653e6760052c01c1c805532f", "Ground Zero" },
            { "65b8d6f5cdde2479cb2a3125", "Ground Zero" },
        };

        // ── Engine map ID → BSG map ID ───────────────────────────────────────
        private static readonly Dictionary<string, string> _engineToBsg = new(StringComparer.OrdinalIgnoreCase)
        {
            { "factory4_day", "55f2d3fd4bdc2d5f408b4567" },
            { "factory4_night", "59fc81d786f774390775787e" },
            { "bigmap", "56f40101d2720b2a4d8b45d6" },
            { "woods", "5704e3c2d2720bac5b8b4567" },
            { "lighthouse", "5704e4dad2720bb55b8b4567" },
            { "shoreline", "5704e554d2720bac5b8b456e" },
            { "labyrinth", "6733700029c367a3d40b02af" },
            { "rezervbase", "5704e5fad2720bc05b8b4567" },
            { "interchange", "5714dbc024597771384a510d" },
            { "tarkovstreets", "5714dc692459777137212e12" },
            { "laboratory", "5b0fc42d86f7744a585f9105" },
            { "Sandbox", "653e6760052c01c1c805532f" },
            { "Sandbox_high", "65b8d6f5cdde2479cb2a3125" },
        };

        // ── Map filter options (for the combo box) ───────────────────────────
        // Built lazily from unique BSG map display names.
        private static string[]? _mapFilterOptions;
        private static string[]? _mapFilterBsgIds;

        // ── Cached grouping (rebuilt only when quest data or filters change) ─
        private static IReadOnlyList<Quest>? _lastQuests;
        private static string _lastFilterBsgId = "";
        private static bool _lastKappaFilter;
        private static bool _lastHideCompleted;
        private static readonly List<(string Trader, List<Quest> Quests)> _cachedGroups = [];
        private static int _cachedShownCount;

        // ── Cached map tags per quest ID (rebuilt with grouping) ─────────────
        private static readonly Dictionary<string, string> _mapTagCache = new(StringComparer.OrdinalIgnoreCase);

        public static void Draw()
        {
            bool isOpen = IsOpen;
            using var scope = PanelWindow.Begin("\u2756 Quests", ref isOpen, new Vector2(500, 580));
            IsOpen = isOpen;
            if (!scope.Visible)
                return;

            DrawFilters();
            ImGui.Separator();

            var questManager = Memory.QuestManager;
            if (questManager is null)
            {
                ImGui.TextColored(ColGrey, "Waiting for game connection...");
                return;
            }

            var activeQuests = questManager.ActiveQuests;
            if (activeQuests.Count == 0)
            {
                ImGui.TextColored(ColGrey, "No active quests found.");
                return;
            }

            // Determine active map filter
            var filterBsgId = GetSelectedFilterBsgId();

            // Rebuild cached grouping only when inputs change
            var kappaFilter = Config.QuestKappaFilter;
            if (!ReferenceEquals(activeQuests, _lastQuests)
                || !string.Equals(filterBsgId, _lastFilterBsgId, StringComparison.Ordinal)
                || kappaFilter != _lastKappaFilter
                || _hideCompleted != _lastHideCompleted)
            {
                RebuildGroupCache(activeQuests, filterBsgId, kappaFilter);
            }

            // Status line
            bool inRaid = Memory.InRaid;
            if (inRaid)
            {
                var mapName = GetCurrentMapDisplayName();
                ImGui.TextColored(ColCyan, $"In Raid \u2014 {mapName}");
            }
            else
            {
                ImGui.TextColored(ColGrey, "Lobby");
            }
            ImGui.SameLine();
            ImGui.TextColored(ColDim, $"({_cachedShownCount} quests)");
            ImGui.Separator();

            if (_cachedShownCount == 0)
            {
                ImGui.TextColored(ColDim, "  No quests match current filters.");
                return;
            }

            // Draw trader groups from cache
            for (int t = 0; t < _cachedGroups.Count; t++)
            {
                var (trader, quests) = _cachedGroups[t];
                DrawTraderGroup(trader, quests);
            }
        }

        #region Filters

        private static void DrawFilters()
        {
            // Row 1: toggle filters
            ImGui.Checkbox("Keys", ref _showKeys);
            ImGui.SameLine();
            ImGui.Checkbox("Items", ref _showRequiredItems);
            ImGui.SameLine();
            ImGui.Checkbox("Hide Done", ref _hideCompleted);

            var kappaFilter = Config.QuestKappaFilter;
            if (ImGui.Checkbox("Kappa Only", ref kappaFilter))
                Config.QuestKappaFilter = kappaFilter;
            ImGui.SameLine();
            var showOptional = Config.QuestShowOptional;
            if (ImGui.Checkbox("Show Optional", ref showOptional))
                Config.QuestShowOptional = showOptional;

            // Row 2: selected-quest controls
            var selectedOnly = Config.QuestSelectedOnly;
            if (ImGui.Checkbox("Selected Only", ref selectedOnly))
            {
                Config.QuestSelectedOnly = selectedOnly;
                Config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("When enabled, only the selected quest's items and zones are drawn on the radar.\nRight-click a quest to select it.");

            if (!string.IsNullOrEmpty(Config.QuestSelectedId))
            {
                ImGui.SameLine();
                var selName = GetQuestNameById(Config.QuestSelectedId);
                ImGui.TextColored(ColKappa, $"\u2605 {selName}");
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear"))
                {
                    Config.QuestSelectedId = "";
                    Config.Save();
                }
            }

            // Row 3: map filter combo
            EnsureMapFilterOptions();
            if (_mapFilterOptions is not null)
            {
                ImGui.SetNextItemWidth(180);
                ImGui.Combo("Map", ref _selectedMapIndex, _mapFilterOptions, _mapFilterOptions.Length);
            }

            // Row 4: hidden quests management
            var hiddenCount = Config.QuestBlacklist?.Count ?? 0;
            if (hiddenCount > 0)
            {
                ImGui.SameLine();
                if (ImGui.Button($"Hidden ({hiddenCount})"))
                    ImGui.OpenPopup("quest_hidden_popup");
            }

            DrawHiddenQuestsPopup();
        }

        private static void DrawHiddenQuestsPopup()
        {
            ImGui.SetNextWindowSize(new Vector2(380, 320), ImGuiCond.Appearing);
            if (!ImGui.BeginPopup("quest_hidden_popup"))
                return;

            ImGui.TextColored(ColYellow, "Hidden Quests");
            ImGui.Separator();

            var blacklist = Config.QuestBlacklist;
            if (blacklist is null || blacklist.Count == 0)
            {
                ImGui.TextColored(ColGrey, "No hidden quests.");
                ImGui.EndPopup();
                return;
            }

            if (ImGui.Button("Clear All"))
            {
                blacklist.Clear();
                Config.Save();
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }
            ImGui.SameLine();
            ImGui.TextColored(ColGrey, $"{blacklist.Count} hidden");

            ImGui.Separator();
            ImGui.BeginChild("hidden_list", new Vector2(0, 220), ImGuiChildFlags.Borders);

            // Iterate over a snapshot so we can mutate the underlying list safely.
            string? toRestore = null;
            for (int i = 0; i < blacklist.Count; i++)
            {
                var qid = blacklist[i];
                ImGui.PushID(qid);
                var name = GetQuestNameById(qid);
                if (ImGui.SmallButton("Show"))
                    toRestore = qid;
                ImGui.SameLine();
                ImGui.TextUnformatted(name);
                ImGui.PopID();
            }
            ImGui.EndChild();

            if (toRestore is not null)
            {
                blacklist.RemoveAll(q => string.Equals(q, toRestore, StringComparison.OrdinalIgnoreCase));
                Config.Save();
            }

            ImGui.EndPopup();
        }

        private static string GetQuestNameById(string questId)
        {
            if (EftDataManager.TaskData.TryGetValue(questId, out var task))
                return task.Name ?? questId;
            return questId;
        }

        private static void EnsureMapFilterOptions()
        {
            if (_mapFilterOptions is not null)
                return;

            // Collect unique display names from BSG map IDs
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            var ids = new List<string>();

            // Add "All Maps" and "Current Map"
            names.Add("All Maps");
            ids.Add("");
            names.Add("Current Map");
            ids.Add("__current__");

            // Add each unique map
            foreach (var kvp in _bsgMapNames)
            {
                if (seen.Add(kvp.Value))
                {
                    names.Add(kvp.Value);
                    ids.Add(kvp.Key);
                }
            }

            _mapFilterOptions = names.ToArray();
            _mapFilterBsgIds = ids.ToArray();
        }

        private static string GetSelectedFilterBsgId()
        {
            if (_mapFilterBsgIds is null || _selectedMapIndex < 0 || _selectedMapIndex >= _mapFilterBsgIds.Length)
                return "";

            var id = _mapFilterBsgIds[_selectedMapIndex];
            if (id == "__current__")
            {
                // Resolve current map to BSG ID
                var engineMapId = Memory.MapID;
                if (!string.IsNullOrEmpty(engineMapId) && _engineToBsg.TryGetValue(engineMapId, out var bsgId))
                    return bsgId;
                return ""; // No map = show all
            }
            return id;
        }

        #endregion

        #region Trader Group

        private static void DrawTraderGroup(string traderName, List<Quest> quests)
        {
            var isCollapsed = _collapsedTraders.Contains(traderName);
            var icon = isCollapsed ? "\u25b6" : "\u25bc"; // ▶ / ▼

            ImGui.PushID($"trader_{traderName}");
            ImGui.TextColored(ColYellow, icon);
            ImGui.SameLine();
            if (ImGui.Selectable($"{traderName} ({quests.Count})", false))
            {
                if (isCollapsed)
                    _collapsedTraders.Remove(traderName);
                else
                    _collapsedTraders.Add(traderName);
            }

            if (!isCollapsed)
            {
                ImGui.Indent(8f);
                for (int i = 0; i < quests.Count; i++)
                    DrawQuest(quests[i]);
                ImGui.Unindent(8f);
            }

            ImGui.PopID();
        }

        #endregion

        #region Quest Drawing

        private static void DrawQuest(Quest quest)
        {
            var isCollapsed = _collapsedQuests.Contains(quest.Id);
            var allComplete = quest.IsCompleted;

            if (_hideCompleted && allComplete)
                return;

            var collapseIcon = isCollapsed ? "\u25b6" : "\u25bc"; // ▶ / ▼
            var headerCol = allComplete ? ColGreen : ColWhite;
            bool isSelected = string.Equals(Config.QuestSelectedId, quest.Id, StringComparison.OrdinalIgnoreCase);

            ImGui.PushID(quest.Id);

            // Header: icon + kappa star + selection star + name + map tag
            ImGui.TextColored(headerCol, collapseIcon);
            ImGui.SameLine();
            if (quest.KappaRequired)
            {
                ImGui.TextColored(ColKappa, "\u2605");
                ImGui.SameLine();
            }
            if (isSelected)
            {
                ImGui.TextColored(ColCyan, "\u25c9"); // ◉
                ImGui.SameLine();
            }

            // Quest name — clickable to toggle collapse
            var questLabel = quest.Name;
            if (ImGui.Selectable(questLabel, isSelected, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X - 80, 0)))
            {
                if (isCollapsed)
                    _collapsedQuests.Remove(quest.Id);
                else
                    _collapsedQuests.Add(quest.Id);
            }

            // Map tag on the same line (right-aligned)
            var mapTag = GetQuestMapTag(quest.Id);
            if (!string.IsNullOrEmpty(mapTag))
            {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(mapTag).X);
                ImGui.TextColored(ColBlue, mapTag);
            }

            // Right-click context menu for selection / blacklist
            if (ImGui.BeginPopupContextItem("quest_ctx"))
            {
                if (isSelected)
                {
                    if (ImGui.MenuItem("Deselect Quest"))
                    {
                        Config.QuestSelectedId = "";
                        Config.Save();
                    }
                }
                else
                {
                    if (ImGui.MenuItem("Select Quest (Radar)"))
                    {
                        Config.QuestSelectedId = quest.Id;
                        Config.QuestSelectedOnly = true;
                        Config.Save();
                    }
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Hide Quest"))
                {
                    Config.QuestBlacklist.Add(quest.Id);
                    Config.Save();
                }
                ImGui.EndPopup();
            }

            if (!isCollapsed)
            {
                ImGui.Indent(16f);
                DrawObjectives(quest);
                ImGui.Unindent(16f);
            }

            ImGui.PopID();
        }

        private static void DrawObjectives(Quest quest)
        {
            if (!EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) || taskData.Objectives is null)
            {
                // Fallback: use in-memory objective data only
                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    var obj = quest.Objectives[i];
                    if (!Config.QuestShowOptional && obj.Optional)
                        continue;
                    if (_hideCompleted && obj.IsCompleted)
                        continue;

                    DrawObjectiveLine(obj.Description, obj.IsCompleted, obj.Optional);
                }
                return;
            }

            // Use rich API data
            for (int i = 0; i < taskData.Objectives.Count; i++)
            {
                var apiObj = taskData.Objectives[i];
                var isCompleted = !string.IsNullOrEmpty(apiObj.Id) && quest.CompletedConditions.Contains(apiObj.Id);

                var isOptional = apiObj.Optional;
                if (i < quest.Objectives.Count)
                    isOptional = isOptional || quest.Objectives[i].Optional;

                if (!Config.QuestShowOptional && isOptional)
                    continue;
                if (_hideCompleted && isCompleted)
                    continue;

                var description = apiObj.Description ?? "Complete objective";
                DrawObjectiveLine(description, isCompleted, isOptional);

                // Show required keys (doors/containers)
                if (_showKeys && !isCompleted)
                {
                    if (apiObj.RequiredKeys is not null)
                    {
                        for (int k = 0; k < apiObj.RequiredKeys.Count; k++)
                        {
                            var keySlot = apiObj.RequiredKeys[k];
                            for (int a = 0; a < keySlot.Count; a++)
                            {
                                var key = keySlot[a];
                                var keyName = GetItemDisplayName(key.Id, key.ShortName ?? key.Name);
                                ImGui.TextColored(ColMagenta, $"      \u26bf {keyName}");
                            }
                        }
                    }
                }

                // Show marker item (item to place at a zone)
                if (_showRequiredItems && !isCompleted && !isOptional)
                {
                    if (apiObj.MarkerItem is not null && !string.IsNullOrEmpty(apiObj.MarkerItem.Id))
                    {
                        var name = GetItemDisplayName(apiObj.MarkerItem.Id, apiObj.MarkerItem.ShortName ?? apiObj.MarkerItem.Name);
                        ImGui.TextColored(ColOrange, $"      \u2691 {name}");
                    }
                }

                // Show required items (find/turn in)
                if (_showRequiredItems && !isCompleted && !isOptional)
                {
                    if (apiObj.Item is not null && !string.IsNullOrEmpty(apiObj.Item.Id))
                    {
                        var itemName = GetItemDisplayName(apiObj.Item.Id, apiObj.Item.Name);
                        var countTag = apiObj.Count > 1 ? $" x{apiObj.Count}" : "";
                        var firTag = apiObj.FoundInRaid ? " (FIR)" : "";
                        ImGui.TextColored(ColOrange, $"      \u25c7 {itemName}{countTag}{firTag}");
                    }
                    if (apiObj.QuestItem is not null && !string.IsNullOrEmpty(apiObj.QuestItem.Id))
                    {
                        var itemName = apiObj.QuestItem.ShortName ?? apiObj.QuestItem.Name ?? apiObj.QuestItem.Id;
                        var countTag = apiObj.Count > 1 ? $" x{apiObj.Count}" : "";
                        ImGui.TextColored(ColOrange, $"      \u25c7 {itemName}{countTag}");
                    }
                }
            }
        }

        private static void DrawObjectiveLine(string description, bool isCompleted, bool isOptional)
        {
            var checkmark = isCompleted ? "\u2714" : "\u25cb"; // ✔ / ○
            var color = isCompleted ? ColGreen : isOptional ? ColGrey : ColWhite;
            var optionalTag = isOptional ? " [Optional]" : "";

            ImGui.TextColored(color, $"{checkmark} {description}{optionalTag}");
        }

        #endregion

        #region Helpers

        private static void RebuildGroupCache(IReadOnlyList<Quest> activeQuests, string filterBsgId, bool kappaFilter)
        {
            _cachedGroups.Clear();
            _mapTagCache.Clear();
            _cachedShownCount = 0;

            var traderGroups = new Dictionary<string, List<Quest>>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < activeQuests.Count; i++)
            {
                var quest = activeQuests[i];

                if (kappaFilter && !quest.KappaRequired)
                    continue;
                if (_hideCompleted && quest.IsCompleted)
                    continue;
                if (!string.IsNullOrEmpty(filterBsgId) && !IsQuestOnMap(quest.Id, filterBsgId))
                    continue;

                var traderName = GetTraderName(quest.Id);

                if (!traderGroups.TryGetValue(traderName, out var list))
                {
                    list = [];
                    traderGroups[traderName] = list;
                }
                list.Add(quest);
                _cachedShownCount++;

                // Pre-compute map tag
                if (!_mapTagCache.ContainsKey(quest.Id))
                    _mapTagCache[quest.Id] = ComputeQuestMapTag(quest.Id);
            }

            // Sort trader names and build ordered list
            var traderNames = new List<string>(traderGroups.Keys);
            traderNames.Sort(StringComparer.OrdinalIgnoreCase);

            for (int t = 0; t < traderNames.Count; t++)
                _cachedGroups.Add((traderNames[t], traderGroups[traderNames[t]]));

            _lastQuests = activeQuests;
            _lastFilterBsgId = filterBsgId;
            _lastKappaFilter = kappaFilter;
            _lastHideCompleted = _hideCompleted;
        }

        private static string GetTraderName(string questId)
        {
            if (EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                return taskData.Trader?.Name ?? "Unknown";
            return "Unknown";
        }

        /// <summary>
        /// Returns a cached map tag for display next to quest name.
        /// </summary>
        private static string GetQuestMapTag(string questId)
        {
            return _mapTagCache.TryGetValue(questId, out var tag) ? tag : "";
        }

        /// <summary>
        /// Computes a short map tag, e.g. "[Customs]" or "[Any Map]". Called only during cache rebuild.
        /// </summary>
        private static string ComputeQuestMapTag(string questId)
        {
            if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                return "";

            // Quest-level map assignment
            if (taskData.Map is not null && !string.IsNullOrEmpty(taskData.Map.Name))
                return $"[{taskData.Map.Name}]";

            // Collect unique map names from objectives
            HashSet<string>? mapNames = null;
            if (taskData.Objectives is not null)
            {
                for (int i = 0; i < taskData.Objectives.Count; i++)
                {
                    var obj = taskData.Objectives[i];
                    if (obj.Maps is not null)
                    {
                        for (int m = 0; m < obj.Maps.Count; m++)
                        {
                            var mapName = obj.Maps[m].Name;
                            if (!string.IsNullOrEmpty(mapName))
                            {
                                mapNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                mapNames.Add(mapName);
                            }
                        }
                    }
                    if (obj.Zones is not null)
                    {
                        for (int z = 0; z < obj.Zones.Count; z++)
                        {
                            var zoneMap = obj.Zones[z].Map;
                            if (zoneMap is not null && !string.IsNullOrEmpty(zoneMap.Name))
                            {
                                mapNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                mapNames.Add(zoneMap.Name);
                            }
                        }
                    }
                }
            }

            if (mapNames is null || mapNames.Count == 0)
                return "[Any Map]";

            if (mapNames.Count == 1)
            {
                foreach (var n in mapNames)
                    return $"[{n}]";
            }

            if (mapNames.Count <= 3)
            {
                var sorted = new List<string>(mapNames);
                sorted.Sort(StringComparer.OrdinalIgnoreCase);
                return $"[{string.Join(", ", sorted)}]";
            }

            return $"[{mapNames.Count} Maps]";
        }

        private static string GetItemDisplayName(string itemId, string? fallbackName)
        {
            if (EftDataManager.AllItems.TryGetValue(itemId, out var item))
                return item.ShortName ?? item.Name ?? fallbackName ?? "Unknown";

            return fallbackName ?? "Unknown Item";
        }

        private static string GetCurrentMapDisplayName()
        {
            var engineMapId = Memory.MapID;
            if (string.IsNullOrEmpty(engineMapId))
                return "Unknown Map";

            if (_engineToBsg.TryGetValue(engineMapId, out var bsgId) && _bsgMapNames.TryGetValue(bsgId, out var name))
                return name;

            return engineMapId;
        }

        private static bool IsQuestOnMap(string questId, string mapBsgId)
        {
            if (string.IsNullOrEmpty(mapBsgId))
                return true; // No filter = show all

            if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                return false;

            // Check quest-level map assignment
            if (taskData.Map is not null && string.Equals(taskData.Map.Id, mapBsgId, StringComparison.OrdinalIgnoreCase))
                return true;

            // Any-map quests (no map specified) should show for all filters
            bool hasAnyMap = false;
            if (taskData.Map is null || string.IsNullOrEmpty(taskData.Map.Id))
            {
                // Check if objectives have map assignments
                if (taskData.Objectives is not null)
                {
                    for (int i = 0; i < taskData.Objectives.Count; i++)
                    {
                        var obj = taskData.Objectives[i];

                        if (obj.Maps is not null)
                        {
                            for (int m = 0; m < obj.Maps.Count; m++)
                            {
                                hasAnyMap = true;
                                if (string.Equals(obj.Maps[m].Id, mapBsgId, StringComparison.OrdinalIgnoreCase))
                                    return true;
                            }
                        }

                        if (obj.Zones is not null)
                        {
                            for (int z = 0; z < obj.Zones.Count; z++)
                            {
                                var zone = obj.Zones[z];
                                if (zone.Map is not null)
                                {
                                    hasAnyMap = true;
                                    if (string.Equals(zone.Map.Id, mapBsgId, StringComparison.OrdinalIgnoreCase))
                                        return true;
                                }
                            }
                        }
                    }
                }

                // Quest has no map constraint at all — it's an "any map" quest
                if (!hasAnyMap)
                    return true;
            }

            // Check objective-level maps (quest has a map but objectives may point elsewhere)
            if (taskData.Objectives is not null)
            {
                for (int i = 0; i < taskData.Objectives.Count; i++)
                {
                    var obj = taskData.Objectives[i];
                    if (obj.Maps is not null)
                    {
                        for (int m = 0; m < obj.Maps.Count; m++)
                        {
                            if (string.Equals(obj.Maps[m].Id, mapBsgId, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                    if (obj.Zones is not null)
                    {
                        for (int z = 0; z < obj.Zones.Count; z++)
                        {
                            var zoneMap = obj.Zones[z].Map;
                            if (zoneMap is not null &&
                                string.Equals(zoneMap.Id, mapBsgId, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
