using System.Collections.Frozen;

using eft_dma_radar.Silk.Tarkov.Unity;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Reads quest/task data from the local player's profile in memory and resolves
    /// quest zone locations using the tarkov.dev API data (embedded in DEFAULT_DATA.json).
    /// <para>
    /// Refreshed periodically from the registration worker thread. Exposes:
    /// <list type="bullet">
    ///   <item><see cref="ActiveQuests"/> — all quests with Started status</item>
    ///   <item><see cref="RequiredItems"/> — item IDs needed for incomplete objectives</item>
    ///   <item><see cref="LocationConditions"/> — quest zones for the current map</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class QuestManager
    {
        #region Static Fields

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
            { "Sandbox_high", "65b8d6f5cdde2479cb2a3125" },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Cached zone lookups — rebuilt when filter settings change
        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>>? _questZones;
        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>>? _questOutlines;
        private static bool _lastKappaFilter;
        private static bool _lastOptionalFilter;

        #endregion

        #region Instance Fields

        private static SilkConfig Config => SilkProgram.Config;

        private readonly Stopwatch _rateLimit = new();
        private readonly ulong _profilePtr;
        private readonly string _mapId;

        #endregion

        #region Properties

        /// <summary>All currently active quests with objectives and completion status.</summary>
        public IReadOnlyList<Quest> ActiveQuests { get; private set; } = [];

        /// <summary>IDs of all started quests (including blacklisted) for UI filtering.</summary>
        public IReadOnlySet<string> AllStartedQuestIds { get; private set; } = new HashSet<string>();

        /// <summary>Item IDs required for incomplete quest objectives.</summary>
        public IReadOnlySet<string> RequiredItems { get; private set; } = new HashSet<string>();

        /// <summary>Quest zone locations for the current map (incomplete objectives only).</summary>
        public IReadOnlyList<QuestLocation> LocationConditions { get; private set; } = [];

        /// <summary>All completed condition IDs across all active quests.</summary>
        public IReadOnlySet<string> AllCompletedConditions { get; private set; } = new HashSet<string>();

        #endregion

        #region Constructor

        public QuestManager(ulong profilePtr, string mapId)
        {
            _profilePtr = profilePtr;
            _mapId = mapId;
            UpdateZoneCaches();
            Refresh();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Checks if a specific item ID is required for any incomplete quest condition.
        /// </summary>
        public bool IsItemRequired(string itemId) => RequiredItems.Contains(itemId);

        /// <summary>
        /// Refreshes quest data from memory. Rate-limited to once per 2 seconds.
        /// Called from the registration worker thread.
        /// </summary>
        public void Refresh()
        {
            UpdateZoneCaches();

            if (_rateLimit.IsRunning && _rateLimit.Elapsed.TotalSeconds < 2d)
                return;

            try
            {
                RefreshCore();
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "quest_refresh", TimeSpan.FromSeconds(10),
                    $"[QuestManager] Refresh error: {ex.Message}");
            }

            _rateLimit.Restart();
        }

        #endregion

        #region Core Refresh

        private void RefreshCore()
        {
            if (!Memory.TryReadPtr(_profilePtr + Offsets.Profile.QuestsData, out var questsDataPtr, false))
                return;

            if (!Memory.TryReadPtr(questsDataPtr + ManagedList.ItemsPtr, out var listItemsPtr, false))
                return;

            var listCount = Memory.ReadValue<int>(questsDataPtr + ManagedList.Count, false);
            if (listCount <= 0 || listCount > 500)
                return;

            var activeQuests = new List<Quest>();
            var allRequiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allLocationConditions = new List<QuestLocation>();
            var allCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allStartedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var currentMapBsgId = _mapToId.GetValueOrDefault(_mapId, "");

            for (int i = 0; i < listCount; i++)
            {
                if (!Memory.TryReadPtr(
                    listItemsPtr + ManagedArray.FirstElement + (ulong)(i * ManagedArray.ElementSize),
                    out var qDataEntry))
                    continue;

                ProcessQuestEntry(qDataEntry, currentMapBsgId, activeQuests,
                    allRequiredItems, allLocationConditions, allCompletedConditions, allStartedQuestIds);
            }

            ActiveQuests = activeQuests;
            AllStartedQuestIds = allStartedQuestIds;
            RequiredItems = allRequiredItems;
            LocationConditions = allLocationConditions;
            AllCompletedConditions = allCompletedConditions;
        }

        private void ProcessQuestEntry(
            ulong qDataEntry,
            string currentMapBsgId,
            List<Quest> activeQuests,
            HashSet<string> allRequiredItems,
            List<QuestLocation> allLocationConditions,
            HashSet<string> allCompletedConditions,
            HashSet<string> allStartedQuestIds)
        {
            if (!Memory.TryReadValue(qDataEntry + Offsets.QuestData.Status, out int qStatus))
                return;
            if (qStatus != 2) // 2 == Started
                return;

            if (!Memory.TryReadPtr(qDataEntry + Offsets.QuestData.Id, out var qIdPtr))
                return;
            var qId = Memory.ReadUnityString(qIdPtr);
            if (string.IsNullOrEmpty(qId))
                return;

            allStartedQuestIds.Add(qId);

            // Kappa filter
            if (Config.QuestKappaFilter
                && EftDataManager.TaskData.TryGetValue(qId, out var taskCheck)
                && !taskCheck.KappaRequired)
                return;

            // Blacklist filter
            var blacklist = Config.QuestBlacklist;
            for (int b = 0; b < blacklist.Count; b++)
            {
                if (string.Equals(blacklist[b], qId, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            // Read completed conditions from HashSet<MongoID>
            var questCompletedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Memory.TryReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions, out var completedHashSetPtr, false))
                ReadHashSetMongoIds(completedHashSetPtr, questCompletedConditions, allCompletedConditions);

            // Build quest from API data
            var quest = CreateQuestFromApiData(qId, questCompletedConditions, currentMapBsgId, allLocationConditions);
            if (quest is null)
                return;

            activeQuests.Add(quest);

            foreach (var item in quest.RequiredItems)
                allRequiredItems.Add(item);
        }

        #endregion

        #region Quest Construction

        private Quest? CreateQuestFromApiData(
            string questId,
            HashSet<string> completedConditions,
            string currentMapBsgId,
            List<QuestLocation> allLocationConditions)
        {
            if (!EftDataManager.TaskData.TryGetValue(questId, out var taskData))
                return null;

            var objectives = new List<QuestObjective>();
            var requiredItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (taskData.Objectives is not null)
            {
                for (int i = 0; i < taskData.Objectives.Count; i++)
                {
                    var apiObj = taskData.Objectives[i];
                    var isCompleted = !string.IsNullOrEmpty(apiObj.Id) && completedConditions.Contains(apiObj.Id);

                    var objective = new QuestObjective
                    {
                        Id = apiObj.Id ?? "",
                        Type = GetObjectiveType(apiObj.Type),
                        Optional = apiObj.Optional,
                        Description = apiObj.Description ?? "",
                        IsCompleted = isCompleted,
                    };

                    // Collect required item IDs
                    if (!string.IsNullOrEmpty(apiObj.Item?.Id))
                        objective.RequiredItemIds.Add(apiObj.Item.Id);
                    if (!string.IsNullOrEmpty(apiObj.QuestItem?.Id))
                        objective.RequiredItemIds.Add(apiObj.QuestItem.Id);
                    if (!string.IsNullOrEmpty(apiObj.MarkerItem?.Id))
                        objective.RequiredItemIds.Add(apiObj.MarkerItem.Id);

                    // Build location objectives from zone data
                    if (apiObj.Zones is not null)
                    {
                        for (int z = 0; z < apiObj.Zones.Count; z++)
                        {
                            var zone = apiObj.Zones[z];
                            if (zone.Position is null || zone.Map?.Id is null)
                                continue;

                            var loc = CreateQuestLocation(questId, zone.Id, apiObj.Optional, apiObj.Id);
                            if (loc is not null)
                                objective.LocationObjectives.Add(loc);
                        }
                    }

                    // Track incomplete objectives
                    if (!isCompleted)
                    {
                        for (int ri = 0; ri < objective.RequiredItemIds.Count; ri++)
                            requiredItems.Add(objective.RequiredItemIds[ri]);

                        for (int li = 0; li < objective.LocationObjectives.Count; li++)
                            allLocationConditions.Add(objective.LocationObjectives[li]);
                    }

                    objectives.Add(objective);
                }
            }

            return new Quest
            {
                Id = questId,
                Name = taskData.Name ?? "Unknown Quest",
                KappaRequired = taskData.KappaRequired,
                Objectives = objectives,
                RequiredItems = requiredItems,
                CompletedConditions = completedConditions,
            };
        }

        private static QuestObjectiveType GetObjectiveType(string? apiType)
        {
            return apiType?.ToLowerInvariant() switch
            {
                "find" or "giveitem" => QuestObjectiveType.FindItem,
                "mark" or "plantitem" => QuestObjectiveType.PlaceItem,
                "visit" => QuestObjectiveType.VisitLocation,
                _ => QuestObjectiveType.Other,
            };
        }

        #endregion

        #region Quest Zones

        private QuestLocation? CreateQuestLocation(string questId, string zoneId, bool optional, string? objectiveId)
        {
            if (!_mapToId.TryGetValue(_mapId, out var bsgMapId))
                return null;

            if (_questZones is null || !_questZones.TryGetValue(bsgMapId, out var zones))
                return null;

            if (!zones.TryGetValue(zoneId, out var position))
                return null;

            // Try outline first
            if (_questOutlines is not null
                && _questOutlines.TryGetValue(bsgMapId, out var outlines)
                && outlines.TryGetValue(zoneId, out var outline))
            {
                return new QuestLocation(questId, zoneId, position, outline, optional, objectiveId ?? zoneId);
            }

            return new QuestLocation(questId, zoneId, position, optional, objectiveId ?? zoneId);
        }

        /// <summary>
        /// Rebuilds zone/outline caches when filter settings change or on first call.
        /// </summary>
        private static void UpdateZoneCaches()
        {
            bool kappaChanged = _lastKappaFilter != SilkProgram.Config.QuestKappaFilter;
            bool optionalChanged = _lastOptionalFilter != SilkProgram.Config.QuestShowOptional;

            if (_questZones is not null && !kappaChanged && !optionalChanged)
            {
                // Retry if zones are empty but TaskData is available (API data loaded late)
                if (_questZones.Count > 0 || EftDataManager.TaskData.Count == 0)
                    return;
            }

            _questZones = BuildQuestZones();
            _questOutlines = BuildQuestOutlines();
            _lastKappaFilter = SilkProgram.Config.QuestKappaFilter;
            _lastOptionalFilter = SilkProgram.Config.QuestShowOptional;
        }

        /// <summary>
        /// Builds a map: BSG map ID → (zone ID → world position).
        /// Uses manual loops instead of LINQ for performance.
        /// </summary>
        private static FrozenDictionary<string, FrozenDictionary<string, Vector3>> BuildQuestZones()
        {
            var result = new Dictionary<string, Dictionary<string, Vector3>>(StringComparer.OrdinalIgnoreCase);
            bool kappaOnly = SilkProgram.Config.QuestKappaFilter;
            bool includeOptional = SilkProgram.Config.QuestShowOptional;

            foreach (var task in EftDataManager.TaskData.Values)
            {
                if (kappaOnly && !task.KappaRequired)
                    continue;
                if (task.Objectives is null)
                    continue;

                for (int i = 0; i < task.Objectives.Count; i++)
                {
                    var obj = task.Objectives[i];
                    if (!includeOptional && obj.Optional)
                        continue;
                    if (obj.Zones is null)
                        continue;

                    for (int z = 0; z < obj.Zones.Count; z++)
                    {
                        var zone = obj.Zones[z];
                        if (zone.Position is null || zone.Map?.Id is null)
                            continue;

                        if (!result.TryGetValue(zone.Map.Id, out var mapZones))
                        {
                            mapZones = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
                            result[zone.Map.Id] = mapZones;
                        }

                        mapZones.TryAdd(zone.Id, new Vector3(zone.Position.X, zone.Position.Y, zone.Position.Z));
                    }
                }
            }

            var frozen = new Dictionary<string, FrozenDictionary<string, Vector3>>(result.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (mapId, zones) in result)
                frozen[mapId] = zones.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            return frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds a map: BSG map ID → (zone ID → outline vertices).
        /// Uses manual loops instead of LINQ for performance.
        /// </summary>
        private static FrozenDictionary<string, FrozenDictionary<string, List<Vector3>>> BuildQuestOutlines()
        {
            var result = new Dictionary<string, Dictionary<string, List<Vector3>>>(StringComparer.OrdinalIgnoreCase);
            bool kappaOnly = SilkProgram.Config.QuestKappaFilter;
            bool includeOptional = SilkProgram.Config.QuestShowOptional;

            foreach (var task in EftDataManager.TaskData.Values)
            {
                if (kappaOnly && !task.KappaRequired)
                    continue;
                if (task.Objectives is null)
                    continue;

                for (int i = 0; i < task.Objectives.Count; i++)
                {
                    var obj = task.Objectives[i];
                    if (!includeOptional && obj.Optional)
                        continue;
                    if (obj.Zones is null)
                        continue;

                    for (int z = 0; z < obj.Zones.Count; z++)
                    {
                        var zone = obj.Zones[z];
                        if (zone.Outline is null || zone.Outline.Count < 3 || zone.Map?.Id is null)
                            continue;

                        if (!result.TryGetValue(zone.Map.Id, out var mapOutlines))
                        {
                            mapOutlines = new Dictionary<string, List<Vector3>>(StringComparer.OrdinalIgnoreCase);
                            result[zone.Map.Id] = mapOutlines;
                        }

                        if (!mapOutlines.ContainsKey(zone.Id))
                        {
                            var vertices = new List<Vector3>(zone.Outline.Count);
                            for (int v = 0; v < zone.Outline.Count; v++)
                            {
                                var pt = zone.Outline[v];
                                vertices.Add(new Vector3(pt.X, pt.Y, pt.Z));
                            }
                            mapOutlines[zone.Id] = vertices;
                        }
                    }
                }
            }

            var frozen = new Dictionary<string, FrozenDictionary<string, List<Vector3>>>(result.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (mapId, outlines) in result)
                frozen[mapId] = outlines.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

            return frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region HashSet<MongoID> Reader

        /// <summary>
        /// Reads MongoIDs from a HashSet&lt;MongoID&gt; in memory and adds them to the result sets.
        /// </summary>
        private static void ReadHashSetMongoIds(ulong hashSetPtr, HashSet<string> questConditions, HashSet<string> allConditions)
        {
            if (hashSetPtr == 0)
                return;

            if (!Memory.TryReadPtr(hashSetPtr + IL2CPPHashSet.Entries, out var entriesPtr, false))
                return;

            if (!Memory.TryReadValue(hashSetPtr + IL2CPPHashSet.Count, out int hashCount, false))
                return;

            // Fallback count offsets if primary fails
            if (hashCount <= 0 || hashCount > 100)
                Memory.TryReadValue(hashSetPtr + 0x20, out hashCount, false);
            if (hashCount <= 0 || hashCount > 100)
                Memory.TryReadValue(hashSetPtr + 0x3C, out hashCount, false);

            if (hashCount <= 0 || hashCount > 100)
                return;

            int found = 0;
            for (int i = 0; i < hashCount && found < 50; i++)
            {
                var entryOffset = (ulong)(i * IL2CPPHashSet.EntrySize);
                var entryBase = entriesPtr + ManagedArray.FirstElement + entryOffset;

                if (!Memory.TryReadPtr(
                    entryBase + IL2CPPHashSet.EntryValueOffset + MongoID.StringID, out var stringIdPtr, false))
                    continue;

                if (stringIdPtr < 0x10000000)
                    continue;

                var conditionId = Memory.ReadUnityString(stringIdPtr);
                if (!string.IsNullOrEmpty(conditionId) && conditionId.Length > 10 && conditionId.Length < 100)
                {
                    questConditions.Add(conditionId);
                    allConditions.Add(conditionId);
                    found++;
                }
            }
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Represents a quest with its objectives and completion status.
    /// </summary>
    internal sealed class Quest
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool KappaRequired { get; init; }
        public List<QuestObjective> Objectives { get; init; } = [];
        public HashSet<string> RequiredItems { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CompletedConditions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True if all objectives are completed.</summary>
        public bool IsCompleted
        {
            get
            {
                for (int i = 0; i < Objectives.Count; i++)
                {
                    if (!Objectives[i].IsCompleted)
                        return false;
                }
                return Objectives.Count > 0;
            }
        }
    }

    /// <summary>
    /// Represents a quest objective with completion status and requirements.
    /// </summary>
    internal sealed class QuestObjective
    {
        public string Id { get; init; } = string.Empty;
        public QuestObjectiveType Type { get; init; }
        public bool Optional { get; init; }
        public string Description { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public List<string> RequiredItemIds { get; init; } = [];
        public List<QuestLocation> LocationObjectives { get; init; } = [];
    }

    /// <summary>
    /// Types of quest objectives.
    /// </summary>
    internal enum QuestObjectiveType
    {
        FindItem,
        PlaceItem,
        VisitLocation,
        Other,
    }

    #endregion
}
