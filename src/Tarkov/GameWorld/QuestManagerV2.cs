using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using System.Collections.Frozen;
using System.Diagnostics;

using TaskElement = eft_dma_radar.Common.Misc.Data.EftDataManager.TaskElement;

namespace eft_dma_radar.Tarkov.GameWorld
{
    /// <summary>
    /// QuestManagerV2 - Full Memory-based quest reading
    /// 
    /// Key differences from V1:
    /// - Uses correct inline HashSet<MongoID> parsing for CompletedConditions
    /// - Attempts to read quest conditions directly from memory (ConditionsDict)
    /// - Falls back to API data when memory parsing fails
    /// 
    /// Memory structures discovered:
    /// - CompletedConditionsCollection: +0x10 = _backendData (HashSet), +0x18 = _localChanges (HashSet)
    /// - HashSet<MongoID> uses INLINE storage (not pointer-based entries)
    ///   - +0x18: bucket count
    ///   - +0x20: entries start (inline, each 32 bytes)
    ///   - Entry structure: hashCode(4) + next(4) + MongoID._bytes(12) + pad(4) + _stringID ptr(8)
    /// - Dictionary<EQuestStatus, ConditionCollection>: standard IL2CPP Dict
    ///   - Entry: hashCode(4) + next(4) + key(4) + pad(4) + value ptr(8) = 24 bytes
    /// </summary>
    public sealed class QuestManagerV2
    {
        private static Config Config => Program.Config;

        /// <summary>
        /// Enable verbose debug logging.
        /// </summary>
        private const bool DEBUG_ENABLED = false;

        // IL2CPP offsets for QuestData
        private const uint QUEST_DATA_ID = 0x10;
        private const uint QUEST_DATA_STATUS = 0x1c;
        private const uint QUEST_DATA_COMPLETED_CONDITIONS = 0x28;
        private const uint QUEST_DATA_TEMPLATE = 0x38;

        // ConditionCollection offsets
        private const uint QUEST_TEMPLATE_CONDITIONS_DICT = 0x60;
        private const uint CONDITION_COLLECTION_LIST = 0x70;

        // Condition base class offsets
        private const uint CONDITION_ID = 0x10;           // MongoID (inline)
        private const uint CONDITION_ID_STRING = 0x10;    // _stringID in MongoID
        private const uint CONDITION_TARGET = 0x98;       // target[] array
        private const uint CONDITION_ZONE_ID = 0x68;      // zoneId for location conditions

        // HashSet<MongoID> inline structure
        private const int HASHSET_BUCKET_OFFSET = 0x18;
        private const int HASHSET_ENTRIES_START = 0x20;
        private const int HASHSET_ENTRY_SIZE = 0x20;      // 32 bytes per entry
        private const int HASHSET_STRING_OFFSET = 0x18;   // _stringID ptr in entry

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

        static QuestManagerV2()
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
            }
        }

        private readonly Stopwatch _rateLimit = new();
        private readonly ulong _profile;

        public QuestManagerV2(ulong profile)
        {
            _profile = profile;
            Refresh();
        }

        public IReadOnlyList<Quest> ActiveQuests { get; private set; } = new List<Quest>();
        public IReadOnlySet<string> AllStartedQuestIds { get; private set; } = new HashSet<string>();
        public IReadOnlySet<string> RequiredItems { get; private set; } = new HashSet<string>();
        public IReadOnlyList<QuestLocation> LocationConditions { get; private set; } = new List<QuestLocation>();
        public IReadOnlySet<string> AllCompletedConditions { get; private set; } = new HashSet<string>();

        private static string MapID => Memory.MapID ?? "MAPDEFAULT";

        public bool IsItemRequired(string itemId) => RequiredItems.Contains(itemId);

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

            try
            {
                var questsData = Memory.ReadPtr(_profile + Offsets.Profile.QuestsData, false);
                
                // Quest data can be temporarily null mid-raid (expected behavior)
                if (questsData == 0 || !questsData.IsValidVirtualAddress())
                {
                    _rateLimit.Restart();
                    return;
                }
                
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

                DebugLog($"Processing {listCount} quests from memory");

                for (int i = 0; i < listCount; i++)
                {
                    var qDataEntry = Memory.ReadPtr(listItemsPtr + UnityOffsets.ManagedArray.FirstElement + (ulong)(i * UnityOffsets.ManagedArray.ElementSize));
                    if (qDataEntry == 0) continue;

                    try
                    {
                        var qStatus = Memory.ReadValue<int>(qDataEntry + QUEST_DATA_STATUS);
                        if (qStatus != 2) continue; // 2 == Started

                        var qIDPtr = Memory.ReadPtr(qDataEntry + QUEST_DATA_ID);
                        var qID = Memory.ReadUnityString(qIDPtr);
                        if (string.IsNullOrEmpty(qID)) continue;

                        allStartedQuestIds.Add(qID);

                        if (Config.QuestHelper.KappaFilter &&
                            EftDataManager.TaskData.TryGetValue(qID, out var taskElement) &&
                            !taskElement.KappaRequired)
                            continue;

                        if (Config.QuestHelper.BlacklistedQuests.Contains(qID, StringComparer.OrdinalIgnoreCase))
                            continue;

                        // Read completed conditions using correct inline HashSet parsing
                        var completedCollectionPtr = Memory.ReadPtr(qDataEntry + QUEST_DATA_COMPLETED_CONDITIONS, false);
                        var questCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        
                        if (completedCollectionPtr != 0)
                        {
                            // CompletedConditionsCollection has two HashSets:
                            // Read CompletedConditionsCollection using centralized offsets
                            var backendPtr = Memory.ReadPtr(completedCollectionPtr + Offsets.CompletedConditionsCollection.BackendData, false);
                            var localPtr = Memory.ReadPtr(completedCollectionPtr + Offsets.CompletedConditionsCollection.LocalChanges, false);
                            
                            ReadHashSetMongoIds(backendPtr, questCompletedConditions, allCompletedConditions, qID, "backend");
                            ReadHashSetMongoIds(localPtr, questCompletedConditions, allCompletedConditions, qID, "local");
                        }

                        // Try to create quest from memory, fallback to API
                        var quest = CreateQuestFromMemory(qID, qDataEntry, questCompletedConditions);
                        if (quest != null)
                        {
                            activeQuests.Add(quest);

                            foreach (var item in quest.RequiredItems)
                                allRequiredItems.Add(item);

                            foreach (var objective in quest.Objectives)
                            {
                                if (!objective.IsCompleted)
                                    allLocationConditions.AddRange(objective.LocationObjectives);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog($"Error parsing quest at 0x{qDataEntry:X}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Refresh error: {ex.Message}");
            }

            ActiveQuests = activeQuests;
            AllStartedQuestIds = allStartedQuestIds;
            RequiredItems = allRequiredItems;
            LocationConditions = allLocationConditions;
            AllCompletedConditions = allCompletedConditions;

            DebugLog($"Loaded {activeQuests.Count} quests, {allCompletedConditions.Count} completed conditions");

            _rateLimit.Restart();
        }

        /// <summary>
        /// Reads MongoIDs from a HashSet<MongoID> using inline storage pattern.
        /// </summary>
        private static void ReadHashSetMongoIds(
            ulong hashSetPtr, 
            HashSet<string> questConditions, 
            HashSet<string> allConditions, 
            string questId, 
            string source)
        {
            if (hashSetPtr == 0) return;

            try
            {
                // HashSet<MongoID> with INLINE storage:
                // +0x18: bucket count (capacity, small prime like 3, 7, 17)
                // +0x20: Entry[0] starts here (entries are INLINE, not pointer array)
                // Each entry is 32 bytes (0x20):
                //   +0x00: hashCode (4) + next (4) = 8 bytes
                //   +0x08: MongoID._bytes (12 bytes raw, inline)
                //   +0x14: padding (4 bytes)
                //   +0x18: MongoID._stringID pointer (8 bytes)
                
                var bucketCount = Memory.ReadValue<int>(hashSetPtr + HASHSET_BUCKET_OFFSET, false);
                
                // Bucket count should be a small prime: 3, 7, 11, 17, 23, etc.
                if (bucketCount <= 0 || bucketCount > 100)
                    return;

                int foundCount = 0;
                for (int i = 0; i < bucketCount && foundCount < 50; i++)
                {
                    var entryBase = hashSetPtr + HASHSET_ENTRIES_START + (ulong)(i * HASHSET_ENTRY_SIZE);
                    
                    // Check if entry is valid (hashCode != -1)
                    var hashCode = Memory.ReadValue<int>(entryBase, false);
                    if (hashCode == -1 || hashCode == 0) continue;
                    
                    // Read string pointer at entry + 0x18
                    var stringPtr = Memory.ReadPtr(entryBase + HASHSET_STRING_OFFSET, false);
                    if (stringPtr == 0 || stringPtr < 0x10000000) continue;
                    
                    var conditionId = Memory.ReadUnityString(stringPtr);
                    if (!string.IsNullOrEmpty(conditionId) && conditionId.Length > 10 && conditionId.Length < 100)
                    {
                        questConditions.Add(conditionId);
                        allConditions.Add(conditionId);
                        foundCount++;
                    }
                }
            }
            catch { /* Ignore read errors */ }
        }

        /// <summary>
        /// Create quest by reading from memory, with API fallback.
        /// </summary>
        private Quest CreateQuestFromMemory(string questId, ulong qDataEntry, HashSet<string> completedConditions)
        {
            if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                return null;

            var quest = new Quest
            {
                Id = questId,
                Name = taskData.Name ?? "Unknown Quest",
                KappaRequired = taskData.KappaRequired,
                CompletedConditions = completedConditions,
                Objectives = new List<QuestObjective>()
            };

            // Try to read conditions from memory
            bool memorySuccess = TryReadConditionsFromMemory(qDataEntry, questId, completedConditions, quest);
            
            // If memory parsing failed or returned no objectives, use API data
            if (!memorySuccess || quest.Objectives.Count == 0)
            {
                quest.Objectives.Clear();
                PopulateObjectivesFromAPI(quest, taskData, completedConditions);
            }

            // Build required items from incomplete objectives
            var requiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in quest.Objectives)
            {
                if (!obj.IsCompleted)
                {
                    foreach (var itemId in obj.RequiredItemIds)
                        requiredItems.Add(itemId);
                }
            }
            quest.RequiredItems = requiredItems;

            return quest;
        }

        /// <summary>
        /// Attempts to read quest conditions directly from memory.
        /// Returns true if at least one condition was successfully parsed.
        /// </summary>
        private bool TryReadConditionsFromMemory(ulong qDataEntry, string questId, HashSet<string> completedConditions, Quest quest)
        {
            try
            {
                var qTemplate = Memory.ReadPtr(qDataEntry + QUEST_DATA_TEMPLATE, false);
                if (qTemplate == 0) return false;

                var conditionsDict = Memory.ReadPtr(qTemplate + QUEST_TEMPLATE_CONDITIONS_DICT, false);
                if (conditionsDict == 0) return false;

                // Dictionary<EQuestStatus, ConditionCollection> using centralized offsets
                var dictEntriesPtr = Memory.ReadPtr(conditionsDict + UnityOffsets.IL2CPPDictionary.Entries, false);
                var dictCount = Memory.ReadValue<int>(conditionsDict + UnityOffsets.IL2CPPDictionary.Count, false);

                if (dictCount <= 0 || dictCount > 10 || dictEntriesPtr == 0)
                    return false;

                int parsedCount = 0;

                // Dictionary entry: hashCode(4) + next(4) + key(4) + pad(4) + value(8) = 24 bytes
                for (int d = 0; d < dictCount; d++)
                {
                    try
                    {
                        var entryBase = dictEntriesPtr + UnityOffsets.IL2CPPDictionary.EntriesStart + (ulong)(d * UnityOffsets.IL2CPPDictionary.EntrySize);
                        var key = Memory.ReadValue<int>(entryBase + 8, false); // EQuestStatus
                        var condCollectionPtr = Memory.ReadPtr(entryBase + UnityOffsets.IL2CPPDictionary.EntryValueOffset, false);

                        if (condCollectionPtr == 0) continue;

                        // _necessaryConditions is an IEnumerable<Condition>
                        // In IL2CPP this can be a List, Array, or LINQ iterator
                        // Try reading as List<T> first
                        var condListPtr = Memory.ReadPtr(condCollectionPtr + CONDITION_COLLECTION_LIST, false);
                        if (condListPtr == 0) continue;

                        // Check if it's a valid List<T> structure using centralized offsets
                        var listItemsPtr = Memory.ReadPtr(condListPtr + UnityOffsets.ManagedList.ItemsPtr, false);
                        var listCount = Memory.ReadValue<int>(condListPtr + UnityOffsets.ManagedList.Count, false);

                        // Validate list structure
                        if (listCount <= 0 || listCount > 50 || listItemsPtr == 0)
                            continue;

                        // Verify it's actually a List by checking the items pointer is valid
                        if (listItemsPtr < 0x10000000)
                            continue;

                        for (int c = 0; c < listCount; c++)
                        {
                            var conditionPtr = Memory.ReadPtr(listItemsPtr + UnityOffsets.ManagedArray.FirstElement + (ulong)(c * UnityOffsets.ManagedArray.ElementSize), false);
                            if (conditionPtr == 0) continue;

                            var objective = ParseConditionFromMemory(conditionPtr, questId, completedConditions);
                            if (objective != null)
                            {
                                quest.Objectives.Add(objective);
                                parsedCount++;
                            }
                        }
                    }
                    catch { /* Skip this dict entry */ }
                }

                return parsedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parse a single Condition object from memory.
        /// </summary>
        private QuestObjective ParseConditionFromMemory(ulong conditionPtr, string questId, HashSet<string> completedConditions)
        {
            try
            {
                // Read condition ID (MongoID is inline at CONDITION_ID)
                var condIdStringPtr = Memory.ReadPtr(conditionPtr + CONDITION_ID + CONDITION_ID_STRING, false);
                var condId = condIdStringPtr != 0 ? Memory.ReadUnityString(condIdStringPtr) : "";
                
                if (string.IsNullOrEmpty(condId))
                    return null;

                // Read class name to determine condition type
                var klassPtr = Memory.ReadPtr(conditionPtr, false);
                var condTypeName = klassPtr != 0 ? ObjectClass.ReadName(klassPtr) : "Unknown";

                var isCompleted = completedConditions.Contains(condId);
                
                // Check API for optional flag (memory doesn't have this easily)
                var isOptional = false;
                string description = "";
                if (EftDataManager.TaskData.TryGetValue(questId, out var taskData) && taskData.Objectives != null)
                {
                    var matchingObj = taskData.Objectives.FirstOrDefault(o => o.Id == condId);
                    if (matchingObj != null)
                    {
                        isOptional = matchingObj.Optional;
                        description = matchingObj.Description ?? "";
                    }
                }

                var objective = new QuestObjective
                {
                    Id = condId,
                    Type = GetConditionType(condTypeName),
                    Optional = isOptional,
                    Description = !string.IsNullOrEmpty(description) ? description : GetConditionDescription(condTypeName),
                    IsCompleted = isCompleted,
                    RequiredItemIds = new List<string>(),
                    LocationObjectives = new List<QuestLocation>()
                };

                // Read target items for find/handover conditions
                if (condTypeName == "ConditionFindItem" || condTypeName == "ConditionHandoverItem")
                {
                    TryReadTargetItems(conditionPtr, objective);
                }

                // Read zone IDs for location conditions
                if (condTypeName == "ConditionVisitPlace" || condTypeName == "ConditionPlaceBeacon" ||
                    condTypeName == "ConditionLeaveItemAtLocation" || condTypeName == "ConditionZone")
                {
                    TryReadZoneLocations(conditionPtr, questId, condId, isOptional, objective);
                }

                return objective;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Try to read target item IDs from a condition.
        /// </summary>
        private void TryReadTargetItems(ulong conditionPtr, QuestObjective objective)
        {
            try
            {
                var targetArray = Memory.ReadPtr(conditionPtr + CONDITION_TARGET, false);
                if (targetArray == 0) return;

                var arrCount = Memory.ReadValue<int>(targetArray + UnityOffsets.ManagedList.Count, false);
                if (arrCount <= 0 || arrCount > 20) return;

                for (int t = 0; t < arrCount; t++)
                {
                    var targetPtr = Memory.ReadPtr(targetArray + UnityOffsets.ManagedArray.FirstElement + (ulong)(t * UnityOffsets.ManagedArray.ElementSize), false);
                    if (targetPtr != 0)
                    {
                        var target = Memory.ReadUnityString(targetPtr);
                        if (!string.IsNullOrEmpty(target))
                            objective.RequiredItemIds.Add(target);
                    }
                }
            }
            catch { /* Ignore */ }
        }

        /// <summary>
        /// Try to read zone locations from a condition.
        /// </summary>
        private void TryReadZoneLocations(ulong conditionPtr, string questId, string objectiveId, bool optional, QuestObjective objective)
        {
            try
            {
                // Try reading zoneId at CONDITION_ZONE_ID
                var zoneIdPtr = Memory.ReadPtr(conditionPtr + CONDITION_ZONE_ID, false);
                if (zoneIdPtr != 0)
                {
                    var zoneId = Memory.ReadUnityString(zoneIdPtr);
                    if (!string.IsNullOrEmpty(zoneId))
                    {
                        var location = CreateQuestLocation(questId, zoneId, optional, objectiveId);
                        if (location != null)
                            objective.LocationObjectives.Add(location);
                    }
                }
            }
            catch { /* Ignore */ }
        }

        /// <summary>
        /// Populate objectives from API data (fallback).
        /// </summary>
        private void PopulateObjectivesFromAPI(Quest quest, TaskElement taskData, HashSet<string> completedConditions)
        {
            if (taskData.Objectives == null) return;

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

                if (apiObj.Zones != null)
                {
                    foreach (var zone in apiObj.Zones)
                    {
                        if (zone.Position != null && zone.Map?.Id != null)
                        {
                            var location = CreateQuestLocation(quest.Id, zone.Id, apiObj.Optional, apiObj.Id);
                            if (location != null)
                                objective.LocationObjectives.Add(location);
                        }
                    }
                }

                quest.Objectives.Add(objective);
            }
        }

        private QuestLocation CreateQuestLocation(string questId, string locationId, bool optional = false, string objectiveId = null)
        {
            if (_mapToId.TryGetValue(MapID, out var id) &&
                _questZones.TryGetValue(id, out var zones) &&
                zones.TryGetValue(locationId, out var location))
            {
                return new QuestLocation(questId, locationId, location, optional, objectiveId ?? locationId);
            }
            return null;
        }

        private static QuestObjectiveType GetConditionType(string condTypeName)
        {
            return condTypeName switch
            {
                "ConditionFindItem" or "ConditionHandoverItem" => QuestObjectiveType.FindItem,
                "ConditionPlaceBeacon" or "ConditionLeaveItemAtLocation" => QuestObjectiveType.PlaceItem,
                "ConditionVisitPlace" => QuestObjectiveType.VisitLocation,
                "ConditionLaunchFlare" => QuestObjectiveType.LaunchFlare,
                "ConditionZone" => QuestObjectiveType.ZoneObjective,
                "ConditionInZone" => QuestObjectiveType.InZone,
                _ => QuestObjectiveType.Other
            };
        }

        private static string GetConditionDescription(string condTypeName)
        {
            return condTypeName switch
            {
                "ConditionFindItem" => "Find items",
                "ConditionHandoverItem" => "Hand over items",
                "ConditionPlaceBeacon" => "Place beacon",
                "ConditionVisitPlace" => "Visit location",
                "ConditionLaunchFlare" => "Launch flare",
                "ConditionZone" => "Complete in zone",
                "ConditionInZone" => "Stay in zone",
                "ConditionCounterCreator" => "Counter objective",
                _ => "Unknown objective"
            };
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
        private static List<string> GetRequiredItemIdsFromApi(TaskElement.ObjectiveElement apiObj)
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

        private static void DebugLog(string message)
        {
            if (DEBUG_ENABLED)
                XMLogging.WriteLine($"[QuestManagerV2] {message}");
        }

        #region Static Cache Methods
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
                        .ToDictionary(zone => zone.id, zone => zone.pos, StringComparer.OrdinalIgnoreCase)
                        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
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
                    outline = zone.Outline.Select(o => new Vector3(o.X, o.Y, o.Z)).ToList()
                }, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(zone => zone.id, zone => zone.outline, StringComparer.OrdinalIgnoreCase)
                        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }
        #endregion
    }
}
