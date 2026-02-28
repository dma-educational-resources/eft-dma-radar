using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Misc.Data.TarkovMarket;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Security.AccessControl;

namespace eft_dma_radar.Tarkov.GameWorld
{
    public sealed class QuestManager
    {
        private static Config Config => Program.Config;

        private static readonly FrozenDictionary<string, string> _mapToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
            { "Sandbox_high", "65b8d6f5cdde2479cb2a3125" }
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> _questZones;
        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> _questOutlines;
        private static bool _lastKappaFilterState;
        private static bool _lastOptionalFilterState;

        static QuestManager()
        {
            UpdateCaches();
        }

        public static void UpdateCaches()
        {
            if (_lastKappaFilterState != Config.QuestHelper.KappaFilter ||
                _lastOptionalFilterState != Config.QuestHelper.OptionalTaskFilter ||
                _questZones == null || _questOutlines == null)
            {
                _questZones = GetQuestZones();
                _questOutlines = GetQuestOutlines();
                _lastKappaFilterState = Config.QuestHelper.KappaFilter;
                _lastOptionalFilterState = Config.QuestHelper.OptionalTaskFilter;
                
                if (DEBUG_QUEST_CONDITIONS)
                {
                    var totalZones = _questZones?.Values.Sum(x => x.Count) ?? 0;
                    var totalOutlines = _questOutlines?.Values.Sum(x => x.Count) ?? 0;
                    XMLogging.WriteLine($"[QuestManager] Cache updated: {totalZones} quest zones, {totalOutlines} zone outlines");
                }
            }
            
            // Retry if zones are empty but TaskData is available (API data loaded late)
            if ((_questZones?.Count == 0 || _questOutlines?.Count == 0) && EftDataManager.TaskData?.Count > 0)
            {
                _questZones = GetQuestZones();
                _questOutlines = GetQuestOutlines();
                
                if (DEBUG_QUEST_CONDITIONS)
                {
                    var totalZones = _questZones?.Values.Sum(x => x.Count) ?? 0;
                    var totalOutlines = _questOutlines?.Values.Sum(x => x.Count) ?? 0;
                    XMLogging.WriteLine($"[QuestManager] Cache retry: {totalZones} quest zones, {totalOutlines} zone outlines");
                }
            }
        }

        public static EntityTypeSettings Settings => Config.EntityTypeSettings.GetSettings("QuestZone");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("QuestZone");

        /// <summary>
        /// Enable verbose debug logging for quest condition reading.
        /// Set to false to disable per-refresh logging (one-time dump still runs).
        /// </summary>
        private const bool DEBUG_QUEST_CONDITIONS = false;
        
        /// <summary>
        /// Flag to ensure comprehensive debug dump only runs once per session.
        /// </summary>
        private static bool _debugDumpComplete = false;

        private readonly Stopwatch _rateLimit = new();
        private readonly ulong _profile;

        public QuestManager(ulong profile)
        {
            _profile = profile;
            Refresh();
        }

        /// <summary>
        /// All currently active quests with their objectives and completion status.
        /// </summary>
        public IReadOnlyList<Quest> ActiveQuests { get; private set; } = new List<Quest>();

        /// <summary>
        /// Contains IDs of all started quests (including blacklisted ones) for UI purposes only.
        /// </summary>
        public IReadOnlySet<string> AllStartedQuestIds { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Contains all item IDs that are required for incomplete quest objectives.
        /// </summary>
        public IReadOnlySet<string> RequiredItems { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Contains all quest locations for the current map.
        /// </summary>
        public IReadOnlyList<QuestLocation> LocationConditions { get; private set; } = new List<QuestLocation>();

        /// <summary>
        /// All completed condition IDs across all active quests.
        /// </summary>
        public IReadOnlySet<string> AllCompletedConditions { get; private set; } = new HashSet<string>();

        /// <summary>
        /// Current Map ID.
        /// </summary>
        private static string MapID
        {
            get
            {
                var id = Memory.MapID;
                id ??= "MAPDEFAULT";
                return id;
            }
        }

        /// <summary>
        /// Checks if a specific item ID is required for any incomplete quest condition.
        /// </summary>
        /// <param name="itemId">The item's BSG ID</param>
        /// <returns>True if this item is required for an incomplete quest condition</returns>
        public bool IsItemRequired(string itemId)
        {
            return RequiredItems.Contains(itemId);
        }

        /// <summary>
        /// Gets all quests that require items on the current map.
        /// </summary>
        public IEnumerable<Quest> GetQuestsForCurrentMap()
        {
            if (!_mapToId.TryGetValue(MapID, out var currentMapId))
                return Enumerable.Empty<Quest>();

            return ActiveQuests.Where(quest =>
            {
                var hasLocationObjective = quest.Objectives.Any(obj =>
                    obj.LocationObjectives.Any(loc => loc.MapId == currentMapId) ||
                    obj.HasLocationRequirement
                );

                if (hasLocationObjective)
                    return true;

                if (EftDataManager.TaskData.TryGetValue(quest.Id, out var taskData) && taskData.Objectives != null)
                {
                    return taskData.Objectives.Any(objective =>
                        objective.Maps != null && objective.Maps.Any(map => map.Id == currentMapId)
                    );
                }

                return false;
            });
        }

        /// <summary>
        /// Gets all other active quests (not specific to current map).
        /// </summary>
        public IEnumerable<Quest> GetOtherQuests()
        {
            var questsForCurrentMap = GetQuestsForCurrentMap().Select(q => q.Id).ToHashSet();
            return ActiveQuests.Where(quest => !questsForCurrentMap.Contains(quest.Id));
        }

        public void Refresh()
        {
            UpdateCaches();

            if (_rateLimit.IsRunning && _rateLimit.Elapsed.TotalSeconds < 2d)
                return;

            var activeQuests = new List<Quest>();
            var allRequiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allLocationConditions = new List<QuestLocation>();
            var allCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allStartedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // IL2CPP hardcoded offsets for quest reading
            var questsData = Memory.ReadPtr(_profile + Offsets.Profile.QuestsData, false);
            
            // Quest data can be temporarily null mid-raid (game unloads it during certain events)
            // This is expected behavior - just skip this refresh cycle
            if (questsData == 0 || !questsData.IsValidVirtualAddress())
            {
                _rateLimit.Restart(); // Rate limit to avoid rapid retries
                return;
            }
            
            // Read list structure using centralized offsets
            var listItemsPtr = Memory.ReadPtr(questsData + UnityOffsets.ManagedList.ItemsPtr, false);
            if (listItemsPtr == 0 || !listItemsPtr.IsValidVirtualAddress())
            {
                _rateLimit.Restart();
                return;
            }
            
            var listCount = Memory.ReadValue<int>(questsData + UnityOffsets.ManagedList.Count, false);
            
            if (listCount <= 0 || listCount > 500)
            {
                _rateLimit.Restart();
                return;
            }
            
            // One-time comprehensive debug dump
            if (!_debugDumpComplete)
            {
                PerformComprehensiveQuestDump(listItemsPtr, listCount);
                _debugDumpComplete = true;
            }

            for (int i = 0; i < listCount; i++)
            {
                var qDataEntry = Memory.ReadPtr(listItemsPtr + UnityOffsets.ManagedArray.FirstElement + (ulong)(i * UnityOffsets.ManagedArray.ElementSize));
                if (qDataEntry == 0) continue;
                
                try
                {
                    var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestData.Status);
                    if (qStatus != 2) // 2 == Started
                        continue;

                    var qIDPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Id);
                    var qID = Memory.ReadUnityString(qIDPtr);
                    
                    if (string.IsNullOrEmpty(qID))
                        continue;

                    allStartedQuestIds.Add(qID);

                    if (Config.QuestHelper.KappaFilter &&
                        EftDataManager.TaskData.TryGetValue(qID, out var taskElement) &&
                        !taskElement.KappaRequired)
                    {
                        continue;
                    }

                    if (Config.QuestHelper.BlacklistedQuests.Contains(qID, StringComparer.OrdinalIgnoreCase))
                        continue;

                    // CompletedConditions is DIRECTLY a HashSet<MongoID> (not a wrapper collection)
                    // This is confirmed in IL2CPP dump and Camera-PWA source
                    var completedHashSetPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions, false);
                    var questCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    if (completedHashSetPtr != 0)
                    {
                        try
                        {
                            if (DEBUG_QUEST_CONDITIONS)
                                XMLogging.WriteLine($"[QuestDebug] Quest {qID}: HashSet @ 0x{completedHashSetPtr:X}");
                            
                            // Read directly from the HashSet<MongoID>
                            ReadHashSetMongoIds(completedHashSetPtr, questCompletedConditions, allCompletedConditions, qID, "completed");
                            
                            if (DEBUG_QUEST_CONDITIONS && questCompletedConditions.Count > 0)
                                XMLogging.WriteLine($"[QuestDebug] Quest {qID}: Found {questCompletedConditions.Count} completed conditions");
                        }
                        catch (Exception ex) 
                        { 
                            if (DEBUG_QUEST_CONDITIONS)
                                XMLogging.WriteLine($"[QuestDebug] Error reading completed conditions for {qID}: {ex.Message}");
                        }
                    }
                    // Note: completedHashSetPtr == 0 is normal for quests with no completed conditions yet

                    var quest = CreateQuestFromGameData(qID, qDataEntry, questCompletedConditions, Offsets.QuestData.Template);
                    if (quest != null)
                    {
                        activeQuests.Add(quest);

                        foreach (var item in quest.RequiredItems)
                        {
                            allRequiredItems.Add(item);
                        }

                        foreach (var objective in quest.Objectives)
                        {
                            if (!objective.IsCompleted)
                                allLocationConditions.AddRange(objective.LocationObjectives);
                        }
                    }
                }
                catch
                {
                    // Silently skip invalid quest entries
                }
            }

            ActiveQuests = activeQuests;
            AllStartedQuestIds = allStartedQuestIds;
            RequiredItems = allRequiredItems;
            LocationConditions = allLocationConditions;
            AllCompletedConditions = allCompletedConditions;

            // Debug: Log LocationConditions count for current map
            if (DEBUG_QUEST_CONDITIONS)
            {
                if (allLocationConditions.Count > 0)
                    XMLogging.WriteLine($"[QuestManager] {allLocationConditions.Count} location objectives for current map ({MapID})");
                else
                    XMLogging.WriteLine($"[QuestManager] No location objectives for current map ({MapID}). Active quests: {activeQuests.Count}");
            }

            if (MainWindow.Window?.GeneralSettingsControl?.QuestItems?.Count != AllStartedQuestIds.Count)
                MainWindow.Window?.GeneralSettingsControl?.RefreshQuestHelper();

            _rateLimit.Restart();
        }

        private Quest CreateQuestFromGameData(string questId, ulong qDataEntry, HashSet<string> completedConditions, uint templateOffset)
        {
            try
            {
                if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                {
                    return null;
                }

                var quest = new Quest
                {
                    Id = questId,
                    Name = taskData.Name ?? "Unknown Quest",
                    KappaRequired = taskData.KappaRequired,
                    CompletedConditions = completedConditions,
                    Objectives = new List<QuestObjective>()
                };

                // Use static TaskData from API instead of parsing from memory
                // Memory parsing of IEnumerable<Condition> is unreliable due to LINQ iterators
                if (taskData.Objectives != null)
                {
                    foreach (var apiObj in taskData.Objectives)
                    {
                        var isCompleted = !string.IsNullOrEmpty(apiObj.Id) && completedConditions.Contains(apiObj.Id);
                        
                        var objective = new QuestObjective
                        {
                            Id = apiObj.Id ?? "",
                            Type = GetObjectiveType(apiObj.Type),
                            Optional = apiObj.Optional,
                            Description = apiObj.Description ?? "",
                            IsCompleted = isCompleted,
                            RequiredItemIds = GetRequiredItemIdsFromApi(apiObj),
                            LocationObjectives = new List<QuestLocation>()
                        };

                        // Add zone locations if available
                        if (apiObj.Zones != null)
                        {
                            foreach (var zone in apiObj.Zones)
                            {
                                if (zone.Position != null && zone.Map?.Id != null)
                                {
                                    // Try to create with outline first (for kill zones with box boundaries)
                                    // Falls back to simple location if no outline data available
                                    var location = CreateQuestLocationWithOutline(questId, zone.Id, apiObj.Optional, apiObj.Id)
                                                ?? CreateQuestLocation(questId, zone.Id, apiObj.Optional, apiObj.Id);
                                    if (location != null)
                                    {
                                        objective.LocationObjectives.Add(location);
                                    }
                                }
                            }
                        }

                        quest.Objectives.Add(objective);
                    }
                }

                // Build required items from incomplete objectives
                var requiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var objective in quest.Objectives)
                {
                    if (!objective.IsCompleted)
                    {
                        foreach (var itemId in objective.RequiredItemIds)
                        {
                            requiredItems.Add(itemId);
                        }
                    }
                }
                quest.RequiredItems = requiredItems;

                return quest;
            }
            catch
            {
                return null;
            }
        }

        private static QuestObjectiveType GetObjectiveType(string apiType)
        {
            return apiType?.ToLowerInvariant() switch
            {
                "find" or "giveitem" => QuestObjectiveType.FindItem,
                "mark" or "plantitem" => QuestObjectiveType.PlaceItem,
                "visit" => QuestObjectiveType.VisitLocation,
                "shoot" or "kill" or "extract" => QuestObjectiveType.Other,
                _ => QuestObjectiveType.Other
            };
        }
        
        /// <summary>
        /// Gets all required item IDs from an API objective.
        /// Checks both 'item' (regular items) and 'questItem' (quest-specific items like Jaeger's Letter).
        /// </summary>
        private static List<string> GetRequiredItemIdsFromApi(EftDataManager.TaskElement.ObjectiveElement apiObj)
        {
            var itemIds = new List<string>();
            
            // Regular item (e.g., "Find 3 Morphine")
            if (!string.IsNullOrEmpty(apiObj.Item?.Id))
                itemIds.Add(apiObj.Item.Id);
            
            // Quest-specific item (e.g., "Jaeger's Letter", "Pocket Watch")
            if (!string.IsNullOrEmpty(apiObj.QuestItem?.Id))
                itemIds.Add(apiObj.QuestItem.Id);
            
            // Marker item (e.g., items to place)
            if (!string.IsNullOrEmpty(apiObj.MarkerItem?.Id))
                itemIds.Add(apiObj.MarkerItem.Id);
            
            return itemIds;
        }

        /// <summary>
        /// Reads MongoIDs from a HashSet&lt;MongoID&gt; in memory and adds them to the result sets.
        /// Based on Camera-PWA's proven approach (Dec 2025).
        /// </summary>
        private static void ReadHashSetMongoIds(ulong hashSetPtr, HashSet<string> questConditions, HashSet<string> allConditions, string questId, string source)
        {
            if (hashSetPtr == 0) return;
            
            try
            {
                // HashSet<MongoID> structure - read the internal entries array pointer
                var entriesPtr = Memory.ReadPtr(hashSetPtr + UnityOffsets.IL2CPPHashSet2.Entries, false);
                var hashCount = Memory.ReadValue<int>(hashSetPtr + UnityOffsets.IL2CPPHashSet2.Count, false);
                
                // Try alternate count offsets if primary fails
                if (hashCount <= 0 || hashCount > 100)
                {
                    // Try 0x20 (common IL2CPP offset)
                    hashCount = Memory.ReadValue<int>(hashSetPtr + 0x20, false);
                    if (hashCount <= 0 || hashCount > 100)
                    {
                        // Try 0x3C (PWA offset)
                        hashCount = Memory.ReadValue<int>(hashSetPtr + 0x3C, false);
                    }
                }
                
                if (entriesPtr == 0 || hashCount <= 0 || hashCount > 100)
                {
                    // count=0 is valid (no completed conditions yet), don't log as error
                    return;
                }
                
                int foundCount = 0;
                // MongoID is a VALUE TYPE (struct), stored inline in the HashSet entry
                // Entry layout: int hashCode (4), int next (4), then MongoID value inline
                // MongoID layout: uint _timeStamp (0x0), ulong _counter (0x8), string _stringID (0x10)
                for (int i = 0; i < hashCount && foundCount < 50; i++)
                {
                    try
                    {
                        // HashSet Entry: hashCode (4), next (4), value (MongoID struct inline)
                        var entryOffset = (ulong)(i * UnityOffsets.IL2CPPHashSet2.EntrySize);
                        var entryBase = entriesPtr + UnityOffsets.ManagedArray.FirstElement + entryOffset;
                        
                        // MongoID value starts at offset 8 in the entry (after hashCode and next)
                        // Within MongoID, _stringID is at offset 0x10
                        var stringIdPtr = Memory.ReadPtr(entryBase + UnityOffsets.IL2CPPHashSet2.EntryValueOffset + UnityOffsets.MongoID.StringID, false);
                        
                        if (stringIdPtr == 0 || stringIdPtr < 0x10000000)
                            continue;

                        var conditionId = Memory.ReadUnityString(stringIdPtr);
                        if (!string.IsNullOrEmpty(conditionId) && conditionId.Length > 10 && conditionId.Length < 100)
                        {
                            questConditions.Add(conditionId);
                            allConditions.Add(conditionId);
                            foundCount++;
                            if (DEBUG_QUEST_CONDITIONS)
                                XMLogging.WriteLine($"[QuestDebug]     ✓ {conditionId}");
                        }
                    }
                    catch
                    {
                        // Skip invalid entries silently
                    }
                }
                
                if (DEBUG_QUEST_CONDITIONS && foundCount > 0)
                    XMLogging.WriteLine($"[QuestDebug]   {source}: Found {foundCount} conditions");
            }
            catch (Exception ex)
            {
                if (DEBUG_QUEST_CONDITIONS)
                    XMLogging.WriteLine($"[QuestDebug]   {source} read error: {ex.Message}");
            }
        }

        /// <summary>
        /// Comprehensive one-time debug dump of all quest data from memory.
        /// Logs extensive information about each quest for analysis.
        /// </summary>
        private void PerformComprehensiveQuestDump(ulong listItemsPtr, int listCount)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine("================================================================");
            sb.AppendLine("       COMPREHENSIVE QUEST DEBUG DUMP (ONE-TIME)");
            sb.AppendLine("================================================================");
            sb.AppendLine($"  Current Map: {MapID,-20} API TaskData Count: {EftDataManager.TaskData?.Count ?? 0,-10}");
            sb.AppendLine($"  Quest List Count: {listCount,-15} ListItemsPtr: 0x{listItemsPtr:X16}");
            sb.AppendLine("================================================================");
            sb.AppendLine();
            
            // Additional offsets for MainQuest detection (from dump analysis)
            const uint QUEST_TEMPLATE_IS_MAIN_QUEST = 0x120;  // bool IsMainQuest
            const uint QUEST_TEMPLATE_TRADER_ID = 0x48;       // string TraderId
            const uint QUEST_TEMPLATE_TYPE = 0x118;           // EQuestType enum
            
            for (int i = 0; i < listCount; i++)
            {
                var qDataEntry = Memory.ReadPtr(listItemsPtr + UnityOffsets.ManagedArray.FirstElement + (ulong)(i * UnityOffsets.ManagedArray.ElementSize));
                if (qDataEntry == 0) continue;
                
                try
                {
                    sb.AppendLine($"-----------------------------------------------------------------------");
                    sb.AppendLine($"  QUEST #{i}");
                    sb.AppendLine($"-----------------------------------------------------------------------");
                    
                    // Read basic quest data
                    var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestData.Status);
                    var qIDPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Id);
                    var qID = Memory.ReadUnityString(qIDPtr) ?? "(null)";
                    var templatePtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Template);
                    var completedPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions);
                    
                    sb.AppendLine($"  QuestData @ 0x{qDataEntry:X16}");
                    sb.AppendLine($"    ID: {qID}");
                    sb.AppendLine($"    Status: {qStatus} ({GetStatusName(qStatus)}) Template @ 0x{templatePtr:X16}");
                    sb.AppendLine($"    CompletedConditions @ 0x{completedPtr:X16}");
                    
                    // Read template data
                    if (templatePtr != 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"    -- TEMPLATE DATA --");
                        
                        try
                        {
                            var namePtr = Memory.ReadPtr(templatePtr + Offsets.QuestTemplate.Name);
                            var questName = (namePtr != 0 && namePtr > 0x10000) ? (Memory.ReadUnityString(namePtr) ?? "(null)") : $"(ptr=0x{namePtr:X})";
                            var conditionsPtr = Memory.ReadPtr(templatePtr + Offsets.QuestTemplate.Conditions);
                            
                            // Read MainQuest flag
                            var isMainQuest = Memory.ReadValue<bool>(templatePtr + QUEST_TEMPLATE_IS_MAIN_QUEST);
                            var questType = Memory.ReadValue<int>(templatePtr + QUEST_TEMPLATE_TYPE);
                            
                            // Try to read trader ID
                            var traderIdPtr = Memory.ReadPtr(templatePtr + QUEST_TEMPLATE_TRADER_ID);
                            var traderId = "(unknown)";
                            if (traderIdPtr != 0 && traderIdPtr > 0x10000)
                                try { traderId = Memory.ReadUnityString(traderIdPtr) ?? "(null)"; } catch { }
                            
                            sb.AppendLine($"    NamePtr @ 0x{namePtr:X} = {questName}");
                            sb.AppendLine($"    IsMainQuest: {isMainQuest} QuestType: {questType} TraderId: {traderId}");
                            sb.AppendLine($"    Conditions @ 0x{conditionsPtr:X16}");
                            
                            // Try to read conditions count
                            if (conditionsPtr != 0)
                            {
                                try
                                {
                                    var condListPtr = Memory.ReadPtr(conditionsPtr + Offsets.QuestConditionsContainer.ConditionsList);
                                    sb.AppendLine($"    ConditionsList @ 0x{condListPtr:X16}");
                                }
                                catch { }
                            }
                        }
                        catch (Exception templateEx)
                        {
                            sb.AppendLine($"    ERROR reading template: {templateEx.Message}");
                        }
                    }
                    
                    // Read completed conditions HashSet
                    if (completedPtr != 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"    -- COMPLETED CONDITIONS HASHSET --");
                        
                        var entriesPtr = Memory.ReadPtr(completedPtr + UnityOffsets.IL2CPPHashSet2.Entries);
                        var count1C = Memory.ReadValue<int>(completedPtr + 0x1C);
                        var count20 = Memory.ReadValue<int>(completedPtr + 0x20);
                        var count3C = Memory.ReadValue<int>(completedPtr + 0x3C);
                        
                        sb.AppendLine($"    Entries @ 0x{entriesPtr:X16}");
                        sb.AppendLine($"    Count@0x1C: {count1C} Count@0x20: {count20} Count@0x3C: {count3C}");
                        
                        // Try to read first few condition IDs
                        var validCount = count1C > 0 && count1C < 100 ? count1C : (count20 > 0 && count20 < 100 ? count20 : count3C);
                        if (entriesPtr != 0 && validCount > 0)
                        {
                            sb.AppendLine($"    Attempting to read {Math.Min(validCount, 5)} entries...");
                            for (int j = 0; j < Math.Min(validCount, 5); j++)
                            {
                                try
                                {
                                    var entryOffset = (ulong)(j * UnityOffsets.IL2CPPHashSet2.EntrySize);
                                    var entryBase = entriesPtr + UnityOffsets.ManagedArray.FirstElement + entryOffset;
                                    var stringIdPtr = Memory.ReadPtr(entryBase + UnityOffsets.IL2CPPHashSet2.EntryValueOffset + UnityOffsets.MongoID.StringID);
                                    
                                    if (stringIdPtr != 0 && stringIdPtr > 0x10000000)
                                    {
                                        var condId = Memory.ReadUnityString(stringIdPtr) ?? "(unreadable)";
                                        sb.AppendLine($"      [{j}] {condId}");
                                    }
                                    else
                                    {
                                        sb.AppendLine($"      [{j}] (invalid ptr: 0x{stringIdPtr:X})");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    sb.AppendLine($"      [{j}] Error: {ex.Message}");
                                }
                            }
                        }
                    }
                    
                    // Check API data availability
                    sb.AppendLine();
                    sb.AppendLine($"    -- API DATA CHECK --");
                    
                    var hasApiData = EftDataManager.TaskData.TryGetValue(qID, out var taskData);
                    sb.AppendLine($"    In API TaskData: {(hasApiData ? "YES" : "NO")}");
                    
                    if (hasApiData && taskData != null)
                    {
                        sb.AppendLine($"    API Name: {taskData.Name ?? "(null)"}");
                        sb.AppendLine($"    KappaRequired: {taskData.KappaRequired} Objectives: {taskData.Objectives?.Count ?? 0}");
                        
                        if (taskData.Objectives != null)
                        {
                            foreach (var obj in taskData.Objectives.Take(3))
                            {
                                var zoneCount = obj.Zones?.Count ?? 0;
                                var itemId = obj.Item?.Id ?? obj.QuestItem?.Id ?? "(none)";
                                sb.AppendLine($"      Obj: {obj.Type} Zones: {zoneCount} Item: {itemId}");
                            }
                            if (taskData.Objectives.Count > 3)
                                sb.AppendLine($"      ... and {taskData.Objectives.Count - 3} more objectives");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"    * QUEST NOT IN API - May be MainQuest/Story or new content");
                    }
                    
                    sb.AppendLine();
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  ERROR reading quest #{i}: {ex.Message}");
                }
            }
            
            // Summary section
            sb.AppendLine("================================================================");
            sb.AppendLine("                           SUMMARY");
            sb.AppendLine("================================================================");
            
            var zonesForMap = _questZones?.TryGetValue(_mapToId.GetValueOrDefault(MapID, ""), out var z) == true ? z.Count : 0;
            var outlinesForMap = _questOutlines?.TryGetValue(_mapToId.GetValueOrDefault(MapID, ""), out var o) == true ? o.Count : 0;
            
            sb.AppendLine($"  Total Quests in Memory: {listCount}");
            sb.AppendLine($"  API TaskData Entries: {EftDataManager.TaskData?.Count ?? 0}");
            sb.AppendLine($"  Quest Zones for {MapID}: {zonesForMap}");
            sb.AppendLine($"  Zone Outlines for {MapID}: {outlinesForMap}");
            sb.AppendLine("================================================================");
            
            if (DEBUG_QUEST_CONDITIONS)
                XMLogging.WriteLine(sb.ToString());
        }
        
        private static string GetStatusName(int status)
        {
            return status switch
            {
                0 => "Locked",
                1 => "AvailableForStart",
                2 => "Started",
                3 => "AvailableForFinish",
                4 => "Success",
                5 => "Fail",
                6 => "FailRestartable",
                7 => "MarkedAsFailed",
                8 => "Expired",
                9 => "AvailableAfter",
                _ => $"Unknown({status})"
            };
        }

        private QuestLocation CreateQuestLocation(string questId, string locationId, bool optional = false, string objectiveId = null)
        {
            // Debug: Log zone lookup issues
            if (!_mapToId.TryGetValue(MapID, out var id))
            {
                if (DEBUG_QUEST_CONDITIONS)
                    XMLogging.WriteLine($"[QuestZone] MapID '{MapID}' not found in _mapToId");
                return null;
            }
            
            if (!_questZones.TryGetValue(id, out var zones))
            {
                if (DEBUG_QUEST_CONDITIONS)
                    XMLogging.WriteLine($"[QuestZone] BSG ID '{id}' not found in _questZones (count: {_questZones?.Count ?? 0})");
                return null;
            }
            
            if (!zones.TryGetValue(locationId, out var location))
            {
                if (DEBUG_QUEST_CONDITIONS)
                    XMLogging.WriteLine($"[QuestZone] Zone '{locationId}' not found for map '{id}' (zones: {zones.Count})");
                return null;
            }
            
            return new QuestLocation(questId, locationId, location, optional, objectiveId ?? locationId);
        }

        private QuestLocation CreateQuestLocationWithOutline(string questId, string locationId, bool optional = false, string objectiveId = null)
        {
            if (_mapToId.TryGetValue(MapID, out var mapId) &&
                _questOutlines.TryGetValue(mapId, out var outlines) &&
                outlines.TryGetValue(locationId, out var outline) &&
                _questZones.TryGetValue(mapId, out var zones) &&
                zones.TryGetValue(locationId, out var location))
            {
                return new QuestLocation(questId, locationId, location, outline, optional, objectiveId ?? locationId);
            }
            return null;
        }

        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> GetQuestZones()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null)
                .SelectMany(task => task.Objectives)
                .Where(objective =>
                {
                    if (!Config.QuestHelper.OptionalTaskFilter && objective.Optional)
                        return false;

                    return objective.Zones is not null;
                })
                .SelectMany(objective => objective.Zones)
                .Where(zone => zone.Position is not null && zone.Map?.Id is not null)
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    pos = new Vector3(zone.Position.X, zone.Position.Y, zone.Position.Z)
                }, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                    .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        zone => zone.id,
                        zone => zone.pos,
                        StringComparer.OrdinalIgnoreCase
                    ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> GetQuestOutlines()
        {
            var tasks = Config.QuestHelper.KappaFilter
                ? EftDataManager.TaskData.Values.Where(task => task.KappaRequired)
                : EftDataManager.TaskData.Values;

            return tasks
                .Where(task => task.Objectives is not null)
                .SelectMany(task => task.Objectives)
                .Where(objective =>
                {
                    if (!Config.QuestHelper.OptionalTaskFilter && objective.Optional)
                        return false;

                    return objective.Zones is not null;
                })
                .SelectMany(objective => objective.Zones)
                .Where(zone => zone.Outline is not null && zone.Map?.Id is not null)
                .GroupBy(zone => zone.Map.Id, zone => new
                {
                    id = zone.Id,
                    outline = zone.Outline.Select(outline => new Vector3(outline.X, outline.Y, outline.Z)).ToList()
                }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            zone => zone.id,
                            zone => zone.outline,
                            StringComparer.OrdinalIgnoreCase
                        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Represents a quest with its objectives and completion status.
    /// </summary>
    public sealed class Quest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool KappaRequired { get; set; }
        public List<QuestObjective> Objectives { get; set; } = new List<QuestObjective>();
        public HashSet<string> RequiredItems { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CompletedConditions { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True if all objectives are completed.
        /// </summary>
        public bool IsCompleted => Objectives.All(o => o.IsCompleted);

        /// <summary>
        /// Number of completed objectives.
        /// </summary>
        public int CompletedObjectivesCount => Objectives.Count(o => o.IsCompleted);

        /// <summary>
        /// Total number of objectives.
        /// </summary>
        public int TotalObjectivesCount => Objectives.Count;
    }

    /// <summary>
    /// Represents a quest objective with its completion status and requirements.
    /// </summary>
    public sealed class QuestObjective
    {
        public string Id { get; set; } = string.Empty;
        public QuestObjectiveType Type { get; set; }
        public bool Optional { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public List<string> RequiredItemIds { get; set; } = new List<string>();
        public List<QuestLocation> LocationObjectives { get; set; } = new List<QuestLocation>();

        /// <summary>
        /// True if this objective has location requirements.
        /// </summary>
        public bool HasLocationRequirement => LocationObjectives.Any();

        /// <summary>
        /// True if this objective requires items.
        /// </summary>
        public bool HasItemRequirement => RequiredItemIds.Any();
    }

    /// <summary>
    /// Types of quest objectives.
    /// </summary>
    public enum QuestObjectiveType
    {
        FindItem,
        PlaceItem,
        VisitLocation,
        LaunchFlare,
        ZoneObjective,
        InZone,
        Other
    }

    /// <summary>
    /// Wraps a Quest Location marker onto the Map GUI.
    /// </summary>
    public sealed class QuestLocation : IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
        private static Config Config => Program.Config;

        private Vector3 _position;
        private List<Vector3> _outline;

        /// <summary>
        /// Original location name.
        /// </summary>
        public string LocationName { get; }

        /// <summary>
        /// Quest name for display purposes.
        /// </summary>
        public string QuestName { get; }

        /// <summary>
        /// Quest this belongs to.
        /// </summary>
        public string QuestID { get; }

        /// <summary>
        /// Objective ID this location is for.
        /// </summary>
        public string ObjectiveId { get; }

        /// <summary>
        /// Map ID this location belongs to.
        /// </summary>
        public string MapId { get; }

        /// <summary>
        /// Whether this quest location comes from an optional objective.
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Quest location outlines (if any).
        /// </summary>
        public List<Vector3> Outline => _outline;

        public QuestLocation(string questId, string locationName, Vector3 position, bool optional = false, string objectiveId = null)
        {
            QuestID = questId;
            LocationName = locationName;
            ObjectiveId = objectiveId ?? locationName;
            _position = position;
            Optional = optional;
            MapId = GetCurrentMapDisplayId();

            if (EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                QuestName = taskData.Name ?? locationName;
            else
                QuestName = locationName;
        }

        public QuestLocation(string questId, string locationName, Vector3 position, List<Vector3> outline, bool optional = false, string objectiveId = null)
        {
            QuestID = questId;
            LocationName = locationName;
            ObjectiveId = objectiveId ?? locationName;
            _position = position;
            _outline = outline;
            Optional = optional;
            MapId = GetCurrentMapDisplayId();

            if (EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                QuestName = taskData.Name ?? locationName;
            else
                QuestName = locationName;
        }

        private string GetCurrentMapDisplayId()
        {
            var mapId = Memory.MapID ?? "unknown";
            return mapId switch
            {
                "factory4_day" => "55f2d3fd4bdc2d5f408b4567",
                "factory4_night" => "59fc81d786f774390775787e",
                "bigmap" => "56f40101d2720b2a4d8b45d6",
                "woods" => "5704e3c2d2720bac5b8b4567",
                "lighthouse" => "5704e4dad2720bb55b8b4567",
                "shoreline" => "5704e554d2720bac5b8b456e",
                "labyrinth" => "6733700029c367a3d40b02af",
                "rezervbase" => "5704e5fad2720bc05b8b4567",
                "interchange" => "5714dbc024597771384a510d",
                "tarkovstreets" => "5714dc692459777137212e12",
                "laboratory" => "5b0fc42d86f7744a585f9105",
                "Sandbox" => "653e6760052c01c1c805532f",
                "Sandbox_high" => "65b8d6f5cdde2479cb2a3125",
                _ => mapId
            };
        }

        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer)
        {
            if (!Config.QuestHelper.OptionalTaskFilter && Optional)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.Settings.RenderDistance)
                return;

            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);

            if (_outline != null && _outline.Count > 2)
                DrawOutline(canvas, mapParams);

            SKPaints.ShapeOutline.StrokeWidth = 2f;
            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, SKPaints.QuestHelperPaint);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 5 * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, SKPaints.QuestHelperPaint);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (QuestManager.Settings.ShowName)
            {
                point.Offset(nameXOffset, nameYOffset);
                if (!string.IsNullOrEmpty(QuestName))
                {
                    canvas.DrawText(QuestName, point, SKPaints.TextOutline);
                    canvas.DrawText(QuestName, point, SKPaints.QuestHelperText);
                }
            }

            if (QuestManager.Settings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = SKPaints.QuestHelperText.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2) - nameXOffset,
                    point.Y + distanceYOffset - nameYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, SKPaints.QuestHelperText);
            }
        }

        private void DrawOutline(SKCanvas canvas, XMMapParams mapParams)
        {
            if (_outline == null || _outline.Count < 3)
                return;

            using var path = new SKPath();
            bool first = true;

            foreach (var vertex in _outline)
            {
                var point = vertex.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
                if (first)
                {
                    path.MoveTo(point);
                    first = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }

            path.Close();

            using var fillPaint = new SKPaint
            {
                Color = SKPaints.QuestHelperPaint.Color.WithAlpha(50),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawPath(path, fillPaint);

            using var strokePaint = new SKPaint
            {
                Color = SKPaints.QuestHelperPaint.Color,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true
            };

            canvas.DrawPath(path, strokePaint);
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            if (!Config.QuestHelper.OptionalTaskFilter && Optional)
                return;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > QuestManager.ESPSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var scale = ESP.Config.FontScale;

            switch (QuestManager.ESPSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, SKPaints.PaintQuestHelperESP);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;
                    using (var thickPaint = new SKPaint
                    {
                        Color = SKPaints.PaintQuestHelperESP.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, SKPaints.PaintQuestHelperESP);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, SKPaints.PaintQuestHelperESP);
                    }
                    break;
            }

            if (QuestManager.ESPSettings.ShowName || QuestManager.ESPSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    QuestManager.ESPSettings.ShowDistance,
                    SKPaints.TextQuestHelperESP,
                    QuestManager.ESPSettings.ShowName ? QuestName : null
                );
            }
        }

        public void DrawMouseover(SKCanvas canvas, XMMapParams mapParams, LocalPlayer localPlayer)
        {
            var lines = new List<string>
            {
                $"Quest: {QuestName}",
                $"Zone: {LocationName}",
                $"---",
                $"Quest ID: {QuestID}",
                $"Objective ID: {ObjectiveId}",
                $"Zone ID: {LocationName}",
            };
            
            // Check completion status from memory
            if (Memory.Game is LocalGameWorld lgw && lgw.QuestManager != null)
            {
                var quest = lgw.QuestManager.ActiveQuests.FirstOrDefault(q => q.Id == QuestID);
                if (quest != null)
                {
                    lines.Add($"---");
                    
                    // Check all possible IDs against CompletedConditions
                    var completedByObjId = quest.CompletedConditions.Contains(ObjectiveId);
                    var completedByZoneId = quest.CompletedConditions.Contains(LocationName);
                    
                    lines.Add($"Completed (by ObjID): {completedByObjId}");
                    lines.Add($"Completed (by ZoneID): {completedByZoneId}");
                    
                    // Show all completed conditions for this quest
                    if (quest.CompletedConditions.Any())
                    {
                        lines.Add($"---");
                        lines.Add($"All Completed Conditions ({quest.CompletedConditions.Count}):");
                        foreach (var cond in quest.CompletedConditions.Take(10)) // Limit to 10
                        {
                            var match = (cond == ObjectiveId || cond == LocationName) ? " <-- THIS" : "";
                            lines.Add($"  ✓ {cond}{match}");
                        }
                        if (quest.CompletedConditions.Count > 10)
                            lines.Add($"  ... and {quest.CompletedConditions.Count - 10} more");
                    }
                }
            }
            
            if (Optional)
                lines.Add($"(Optional Objective)");
            
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines.ToArray());
        }
    }
}