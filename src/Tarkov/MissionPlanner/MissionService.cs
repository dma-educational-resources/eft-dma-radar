using System.Collections.Frozen;
using System.Diagnostics;
using eft_dma_radar.Tarkov.MissionPlanner.Models;
using eft_dma_radar.Common.Misc.Data.TarkovMarket;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.Tarkov.MissionPlanner;

/// <summary>
/// Core session planning service that produces ordered map recommendations with bring lists.
/// Joins quest state from DMA memory with tarkov.dev task metadata to minimize total raids.
/// </summary>
public static class MissionService
{
    /// <summary>
    /// Produces a session plan from active quests, task metadata, stash filter, and settings.
    /// This is the central computation: raw quest IDs + task metadata = actionable plan.
    /// </summary>
    /// <param name="quests">Quests grouped by status from DMA memory</param>
    /// <param name="taskData">Task metadata from tarkov.dev, keyed by task ID</param>
    /// <param name="stash">Stash filter for bring-list item ownership checks</param>
    /// <param name="settings">Planning weight settings (not applied in this phase)</param>
    /// <returns>Ordered session plan with per-map bring lists</returns>
    public static MissionSummary GetSummary(
        AvailableQuests quests,
        FrozenDictionary<string, TaskElement> taskData,
        IStashFilter stash,
        MissionPlannerSettings settings)
    {
        // NOTE: Filter flags from settings are applied below before scoring/ranking.

        // Extract trader names for AvailableForStart and AvailableForFinish quests
        var startTraders = quests.AvailableForStart
            .Where(q => taskData.TryGetValue(q.Id, out var task) && task.Trader?.Name != null)
            .Select(q => taskData[q.Id].Trader.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        var finishTraders = quests.AvailableForFinish
            .Where(q => taskData.TryGetValue(q.Id, out var task) && task.Trader?.Name != null)
            .Select(q => taskData[q.Id].Trader.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        // 1. Find all completable objectives (PLAN-01) - only from Started quests
        var completable = GetCompletableObjectives(quests.Started, taskData).ToList();

        // Apply Kappa filter: restrict to Kappa-required quests only when enabled.
        // KappaRequired is already populated on TaskElement from tarkov.dev API (kappaRequired field).
        // Maps with zero Kappa objectives will naturally disappear from ScoreMaps output.
        if (settings.KappaFilter)
        {
            completable = completable
                .Where(pair => pair.Task.KappaRequired)
                .ToList();
        }

        // 2. Score maps by completable objectives (PLAN-01, PLAN-02)
        var scores = ScoreMaps(completable);

        // 3. Compute unlock counts per map, then rank by unlocks DESC, quest count DESC (PLAN-05)
        ComputeUnlockCounts(scores, quests.Started, taskData);
        var ranked = RankMaps(scores);

        // 4. Apply dependency promotion (PLAN-03, PLAN-04)
        var promoted = ApplyDependencyPromotion(ranked, quests.Started, taskData);

        // 5. Build missions, unlock chains, and bring lists per map
        var mapPlans = promoted.Select((score, index) =>
        {
            var missions = BuildMissionsForMap(score.MapId, completable, quests.Started, stash);
            var unlockedQuests = GetUnlockedQuestsForMap(score.QuestIds, taskData);
            var filteredBringList = BuildFilteredBringList(missions);
            var bringList = BuildBringList(score.MapId, completable, stash);

            return new MapPlan
            {
                MapId = score.MapId,
                MapName = score.MapName,
                IsRecommended = index == 0,
                CompletableObjectiveCount = score.ObjectiveCount,
                ActiveQuestCount = score.QuestIds.Count,
                Missions = missions,
                UnlockedQuests = unlockedQuests,
                FilteredBringList = filteredBringList,
                BringList = bringList
            };
        }).ToList();

        // 6. Build All Maps section: quests with completable objectives that have no map attribution
        var allMapsMissions = BuildAllMapsMissions(completable, quests.Started, stash);

        // 7. Compute Find-in-raid items and Hand-over-items for new UI features
        var firItems = BuildFirItems(quests.Started, taskData);
        var handOverItems = BuildHandOverItems(quests.Started, taskData);

        return new MissionSummary
        {
            Maps = mapPlans,
            AllMapsMissions = allMapsMissions,
            TotalActiveQuests = quests.Started.Count,
            TotalCompletableObjectives = completable.Count,
            StashConnected = stash.IsConnected,
            StashStatus = stash.IsConnected ? null : "Stash not connected - showing all required items",
            AvailableForStartTraders = startTraders,
            AvailableForFinishTraders = finishTraders,
            FirItems = firItems,
            HandOverItems = handOverItems,
            ComputedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// PLAN-01: Identifies objectives that are currently completable.
    /// An objective is completable when (a) its parent quest is active AND (b) the objective's ID
    /// is NOT in CompletedConditions.
    /// </summary>
    private static IEnumerable<(TaskElement Task, TaskElement.ObjectiveElement Objective)> GetCompletableObjectives(
        IReadOnlyList<QuestData> quests,
        FrozenDictionary<string, TaskElement> taskData)
    {
        foreach (var quest in quests)
        {
            // Skip quests without task metadata (per RESEARCH Open Question 4)
            if (!taskData.TryGetValue(quest.Id, out var task))
                continue;

            if (task.Objectives == null)
                continue;

            foreach (var obj in task.Objectives)
            {
                // Objective is completable if NOT already completed
                if (!quest.CompletedConditions.Contains(obj.Id))
                {
                    yield return (task, obj);
                }
            }
        }
    }

    /// <summary>
    /// Normalizes map IDs to handle variants.
    /// Ground Zero has two variants in tarkov.dev data: ground-zero (low-level) and ground-zero-high (21+).
    /// Legacy internal IDs Sandbox/Sandbox_high are also handled for safety.
    /// </summary>
    private static string NormalizeMapId(string mapId) => mapId switch
    {
        "ground-zero-21" => "ground-zero",
        "Sandbox_high"   => "Sandbox",
        _ => mapId
    };

    /// <summary>
    /// Gets the display name for a map, handling Ground Zero merge.
    /// </summary>
    private static string GetMapDisplayName(string normalizedMapId, string originalName) => normalizedMapId switch
    {
        "ground-zero" => "Ground Zero",
        "Sandbox" => "Ground Zero",
        _ => originalName
    };

    /// <summary>
    /// PLAN-01, PLAN-02: Scores each map by counting completable objectives with explicit map attribution.
    /// Objectives with null or empty Maps field contribute to no map's score ("any location" exclusion).
    /// Uses case-insensitive dictionary to handle map ID variations (Pitfall 6).
    /// Merges map variants (Sandbox + Sandbox_high) into single entries.
    /// Fix 1: Quest-level map override — when task.Map is set, use it for ALL objectives of that quest.
    /// Fix 2: Multi-map quest penalty — quests spanning >1 map get reduced objective counts per map.
    /// </summary>
    private static Dictionary<string, MapScore> ScoreMaps(
        IEnumerable<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completableObjectives)
    {
        var scores = new Dictionary<string, MapScore>(StringComparer.OrdinalIgnoreCase);
        // Track per-quest map distribution: questId -> set of distinct normalized map IDs
        var questMapDistribution = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (task, obj) in completableObjectives)
        {
            // Objectives with no maps are "any location" (trader handoffs, etc.) — skip for map scoring.
            // Only override objective maps with quest-level map when the objective HAS map data.
            if (obj.Maps == null || obj.Maps.Count == 0)
                continue;

            // Quest-level map override: if the quest has a map field, use it instead of objective sub-locations.
            // This prevents "The Labyrinth" from overriding the quest's actual map "Customs".
            List<BasicDataElement> effectiveMaps;
            if (task.Map != null)
            {
                effectiveMaps = new List<BasicDataElement> { task.Map };
            }
            else
            {
                effectiveMaps = obj.Maps;
            }

            foreach (var map in effectiveMaps)
            {
                var normalizedId = NormalizeMapId(map.NormalizedName);
                var displayName = GetMapDisplayName(normalizedId, map.Name);

                if (!scores.TryGetValue(normalizedId, out var score))
                {
                    score = new MapScore(normalizedId, displayName);
                    scores[normalizedId] = score;
                }
                score.ObjectiveCount++;
                score.QuestIds.Add(task.Id);

                // Track quest's map distribution for finishable quest detection
                if (!questMapDistribution.TryGetValue(task.Id, out var maps))
                {
                    maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    questMapDistribution[task.Id] = maps;
                }
                maps.Add(normalizedId);
            }
        }

        // Determine finishable quests per map.
        // A quest is "finishable" on a map if ALL its map-attributed objectives are on that single map.
        // Multi-map quests (objectives on >1 distinct map) are NOT finishable on any single map.
        foreach (var (questId, maps) in questMapDistribution)
        {
            if (maps.Count == 1)
            {
                var mapId = maps.First();
                if (scores.TryGetValue(mapId, out var score))
                    score.FinishableQuestIds.Add(questId);
            }
        }

        return scores;
    }

    /// <summary>
    /// Determines whether a completable objective belongs to a specific map.
    /// Checks task-level map first (quest-level override), then falls back to objective-level maps.
    /// </summary>
    private static bool ObjectiveBelongsToMap(TaskElement task, TaskElement.ObjectiveElement obj, string mapId)
    {
        // Objectives with no maps are "any location" (trader handoffs, etc.) — never belong to a specific map,
        // even if the quest itself has a map. Only override objectives that HAVE map data.
        if (obj.Maps == null || obj.Maps.Count == 0)
            return false;

        if (task.Map != null)
        {
            return string.Equals(NormalizeMapId(task.Map.NormalizedName), mapId, StringComparison.OrdinalIgnoreCase);
        }
        return obj.Maps.Any(m =>
            string.Equals(NormalizeMapId(m.NormalizedName), mapId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes how many distinct quests each map would unlock by completing its quests.
    /// Sets UnlockCount on each MapScore before ranking.
    /// </summary>
    private static void ComputeUnlockCounts(
        Dictionary<string, MapScore> scores,
        IReadOnlyList<QuestData> quests,
        FrozenDictionary<string, TaskElement> taskData)
    {
        var activeQuestIds = new HashSet<string>(
            quests.Select(q => q.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var score in scores.Values)
        {
            var unlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in taskData.Values)
            {
                if (activeQuestIds.Contains(task.Id))
                    continue;
                if (task.TaskRequirements == null)
                    continue;

                // Only count unlocks from quests that are finishable on this map.
                // Multi-map quests can't be completed on a single map, so they won't unlock anything here.
                bool wouldBeUnlocked = task.TaskRequirements.Any(req =>
                    req.Task?.Id != null &&
                    score.FinishableQuestIds.Contains(req.Task.Id) &&
                    req.Status != null &&
                    req.Status.Contains("complete", StringComparer.OrdinalIgnoreCase));

                if (wouldBeUnlocked)
                    unlocked.Add(task.Id);
            }

            score.UnlockCount = unlocked.Count;
        }
    }

    /// <summary>
    /// PLAN-05: Base ranking by finishable quest count DESC, unlock count DESC, total quest count DESC.
    /// Finishable quests (completable on this map alone) are prioritized over multi-map quests.
    /// </summary>
    private static List<MapScore> RankMaps(Dictionary<string, MapScore> scores)
    {
        return scores.Values
            .OrderByDescending(s => s.FinishableQuestIds.Count)
            .ThenByDescending(s => s.UnlockCount)
            .ThenByDescending(s => s.QuestIds.Count)
            .ToList();
    }

    /// <summary>
    /// PLAN-03, PLAN-04: Topological sort based on unlock dependencies.
    /// If MapA unlocks quests on MapB, MapA must come before MapB.
    /// This minimizes revisits - when you visit a map, all unlocks from earlier maps are available.
    /// </summary>
    private static List<MapScore> ApplyDependencyPromotion(
        List<MapScore> ranked,
        IReadOnlyList<QuestData> quests,
        FrozenDictionary<string, TaskElement> taskData)
    {
        if (ranked.Count == 0)
            return ranked;

        // Build set of active quest IDs for O(1) lookup
        var activeQuestIds = new HashSet<string>(
            quests.Select(q => q.Id),
            StringComparer.OrdinalIgnoreCase);

        // Build map ID set for quick lookup
        var rankedMapIds = new HashSet<string>(
            ranked.Select(r => r.MapId),
            StringComparer.OrdinalIgnoreCase);

        // Build unlock graph: mapId -> set of mapIds it unlocks quests on
        var unlocksGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in ranked)
        {
            unlocksGraph[map.MapId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Populate unlock graph
        foreach (var map in ranked)
        {
            // Only finishable quests can actually be completed on this map to unlock others.
            var finishableQuestIds = map.FinishableQuestIds;

            foreach (var task in taskData.Values)
            {
                if (activeQuestIds.Contains(task.Id))
                    continue;
                if (task.TaskRequirements == null)
                    continue;

                bool wouldBeUnlocked = task.TaskRequirements.Any(req =>
                    req.Task?.Id != null &&
                    finishableQuestIds.Contains(req.Task.Id) &&
                    req.Status != null &&
                    req.Status.Contains("complete", StringComparer.OrdinalIgnoreCase));

                if (!wouldBeUnlocked)
                    continue;

                // Find which maps this unlocked quest's objectives are on
                if (task.Objectives == null)
                    continue;

                foreach (var obj in task.Objectives)
                {
                    if (obj.Maps == null)
                        continue;
                    foreach (var objMap in obj.Maps)
                    {
                        var normalizedId = NormalizeMapId(objMap.NormalizedName);
                        if (rankedMapIds.Contains(normalizedId) && normalizedId != map.MapId)
                        {
                            unlocksGraph[map.MapId].Add(normalizedId);
                        }
                    }
                }
            }
        }

        // Topological sort with Kahn's algorithm
        // Count incoming edges (how many maps unlock quests on this map)
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapScore in ranked)
        {
            inDegree[mapScore.MapId] = 0;
        }
        foreach (var (fromMap, toMaps) in unlocksGraph)
        {
            foreach (var toMap in toMaps)
            {
                if (inDegree.ContainsKey(toMap))
                    inDegree[toMap]++;
            }
        }

        // Maps with no incoming edges can go first
        // Use priority queue: fewer incoming edges first, then more quests as tiebreaker
        var result = new List<MapScore>();
        var remaining = ranked.ToDictionary(m => m.MapId, StringComparer.OrdinalIgnoreCase);
        var mapByCount = ranked.ToDictionary(m => m.MapId, m => m.QuestIds.Count);

        while (remaining.Count > 0)
        {
            // Find maps with minimum in-degree among remaining
            var minDegree = remaining.Keys.Min(k => inDegree[k]);
            var candidates = remaining.Keys
                .Where(k => inDegree[k] == minDegree)
                .OrderByDescending(k => mapByCount[k])
                .ToList();

            // Pick the one with most quests
            var next = candidates.First();

            result.Add(remaining[next]);
            remaining.Remove(next);

            // Reduce in-degree for maps this one unlocks
            foreach (var toMap in unlocksGraph[next])
            {
                if (inDegree.ContainsKey(toMap))
                    inDegree[toMap]--;
            }
        }

        return result;
    }

    /// <summary>
    /// PLAN-06, PLAN-07: Builds bring list for a specific map.
    /// Includes RequiredKeys (as alternative groups), QuestItems, and MS2000 Markers (inferred from mark/plantItem types).
    /// Excludes FIR items, kill objectives, visit objectives - only items brought FROM stash.
    /// Filters through IStashFilter to exclude owned items.
    /// </summary>
    private static List<BringItem> BuildBringList(
        string mapId,
        List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completableObjectives,
        IStashFilter stash)
    {
        var items = new List<BringItem>();

        foreach (var (task, obj) in completableObjectives)
        {
            // Only objectives on this map (case-insensitive, with normalization)
            // Uses quest-level map override: task.Map takes precedence over obj.Maps
            if (!ObjectiveBelongsToMap(task, obj, mapId))
                continue;

            // RequiredKeys - each slot is a set of alternatives
            if (obj.RequiredKeys != null)
            {
                foreach (var keySlot in obj.RequiredKeys)
                {
                    // Skip if player already owns ANY of the alternatives
                    if (keySlot.Any(k => stash.Owns(k.Id)))
                        continue;

                    items.Add(new BringItem
                    {
                        Alternatives = keySlot.Select(k => k.Name).ToList(),
                        QuestName = task.Name,
                        Type = BringItemType.Key
                    });
                }
            }

            // For mark objectives: use MarkerItem field (e.g., MS2000, etags, etc.)
            // For plantItem objectives: use Item field (e.g., Iskra ration pack, etc.)
            if (obj.Type == "mark")
            {
                if (obj.MarkerItem != null && !stash.Owns(obj.MarkerItem.Id))
                {
                    items.Add(new BringItem { Alternatives = [obj.MarkerItem.Name], QuestName = task.Name, Type = BringItemType.QuestItem });
                }
                else if (obj.MarkerItem == null)
                {
                    // Fallback: some mark objectives might not have MarkerItem set
                    items.Add(new BringItem { Alternatives = ["MS2000 Marker"], QuestName = task.Name, Type = BringItemType.QuestItem });
                }
            }
            else if (obj.Type == "plantItem")
            {
                // PlantItem objectives store the item to plant in Item field
                if (obj.Item != null && !stash.Owns(obj.Item.Id))
                {
                    items.Add(new BringItem { Alternatives = [obj.Item.Name], QuestName = task.Name, Type = BringItemType.QuestItem });
                }
            }

            // QuestItem (for giveQuestItem type or other objectives with non-null QuestItem, excluding plantItem which is handled above)
            if (obj.QuestItem != null && !stash.Owns(obj.QuestItem.Id) && obj.Type != "plantItem")
            {
                items.Add(new BringItem
                {
                    Alternatives = [obj.QuestItem.Name],
                    QuestName = task.Name,
                    Type = BringItemType.QuestItem
                });
            }

            // DO NOT include objective.Item (FIR items are raid objectives, not bring items)
        }

        // Aggregate: same item+quest combination summed by Count
        return items
            .GroupBy(i => (string.Join("|", i.Alternatives), i.QuestName))
            .Select(g => new BringItem
            {
                Alternatives = g.First().Alternatives,
                QuestName = g.First().QuestName,
                Type = g.First().Type,
                Count = g.Sum(x => x.Count)
            })
            .ToList();
    }

    /// <summary>
    /// Builds per-mission data for a specific map.
    /// Groups completable objectives by quest, with filtered bring items per mission.
    /// Applies findQuestItem/giveQuestItem pairing filter to hide giveQuestItem until findQuestItem is complete.
    /// </summary>
    private static List<MissionPlan> BuildMissionsForMap(
        string mapId,
        List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completableObjectives,
        IReadOnlyList<QuestData> quests,
        IStashFilter stash)
    {
        // Build lookup for completed conditions and counters by quest ID
        var completedByQuestId = quests.ToDictionary(q => q.Id, q => q.CompletedConditions, StringComparer.OrdinalIgnoreCase);
        var countersByQuestId = quests.ToDictionary(q => q.Id, q => q.ConditionCounters, StringComparer.OrdinalIgnoreCase);

        // Build task reference dictionary for accessing ALL objectives (not just map-filtered)
        var taskRefById = new Dictionary<string, TaskElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (task, obj) in completableObjectives)
        {
            if (!taskRefById.ContainsKey(task.Id))
                taskRefById[task.Id] = task;
        }

        // Group objectives by task for this map
        var objectivesByTask = new Dictionary<string, List<TaskElement.ObjectiveElement>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (task, obj) in completableObjectives)
        {
            // Only objectives on this map (case-insensitive, with normalization)
            // Uses quest-level map override: task.Map takes precedence over obj.Maps
            if (!ObjectiveBelongsToMap(task, obj, mapId))
                continue;

            if (!objectivesByTask.TryGetValue(task.Id, out var list))
            {
                list = new List<TaskElement.ObjectiveElement>();
                objectivesByTask[task.Id] = list;
            }
            list.Add(obj);
        }

        // Build MissionPlan for each task
        var missions = new List<MissionPlan>();

        // Need task names from completableObjectives
        var taskNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (task, obj) in completableObjectives)
        {
            if (!taskNames.ContainsKey(task.Id))
                taskNames[task.Id] = task.Name;
        }

        foreach (var (taskId, objectives) in objectivesByTask)
        {
            var taskName = taskNames.GetValueOrDefault(taskId, taskId);
            var bringItems = BuildBringListForMission(objectives, taskName, stash);

            // Build findQuestItem pairing lookup from ALL task objectives (not just map-filtered ones)
            var taskRef = taskRefById.GetValueOrDefault(taskId);
            var allObjectives = taskRef?.Objectives ?? new List<TaskElement.ObjectiveElement>();
            var findLookup = allObjectives
                .Where(o => o.Type == "findQuestItem" && o.QuestItem != null)
                .ToDictionary(o => o.QuestItem!.Id, o => o.Id, StringComparer.OrdinalIgnoreCase);

            var completedSet = completedByQuestId.GetValueOrDefault(taskId) ?? new HashSet<string>();
            var questCounters = countersByQuestId.GetValueOrDefault(taskId);

            var filteredObjectives = new List<ObjectiveInfo>();
            foreach (var o in objectives.Where(o => !completedSet.Contains(o.Id)))
            {
                if (o.Type == "giveQuestItem" && o.QuestItem != null)
                {
                    if (findLookup.TryGetValue(o.QuestItem.Id, out var findObjId))
                    {
                        // Paired: only show giveQuestItem once findQuestItem is done
                        if (!completedSet.Contains(findObjId))
                            continue; // Hide — findQuestItem not yet complete
                        // findQuestItem is done: include giveQuestItem (findQuestItem is already filtered out by completedSet)
                    }
                    // No pair found: show giveQuestItem immediately
                }
                filteredObjectives.Add(new ObjectiveInfo(
                    o.Id,
                    o.Description,
                    false,
                    questCounters != null && questCounters.TryGetValue(o.Id, out var cnt) ? cnt : 0,
                    o.Count,
                    o.Type
                ));
            }

            missions.Add(new MissionPlan
            {
                MissionName = taskName,
                Objectives = filteredObjectives,
                BringItems = bringItems
            });
        }

        return missions;
    }

    /// <summary>
    /// Builds filtered bring list for a specific mission's objectives.
    /// FILTER RULE: Only include items that must be BROUGHT INTO raid:
    /// - INCLUDE: RequiredKeys (keys to access areas)
    /// - INCLUDE: QuestItem where objective type is "giveQuestItem" or "plant" (items to hand over or place)
    /// - INCLUDE: MS2000 Marker where objective type is "mark" or "plantItem" (inferred, no QuestItem field)
    /// - EXCLUDE: objective.Item (these are FIR items to FIND in raid)
    /// - EXCLUDE: QuestItem for "findQuestItem" type (need to find, not bring)
    /// </summary>
    private static List<BringItem> BuildBringListForMission(
        List<TaskElement.ObjectiveElement> objectives,
        string taskName,
        IStashFilter stash)
    {
        var items = new List<BringItem>();

        foreach (var obj in objectives)
        {
            // RequiredKeys - always include (keys to access areas, always brought in)
            if (obj.RequiredKeys != null)
            {
                foreach (var keySlot in obj.RequiredKeys)
                {
                    // Skip if player already owns ANY of the alternatives
                    if (keySlot.Any(k => stash.Owns(k.Id)))
                        continue;

                    items.Add(new BringItem
                    {
                        Alternatives = keySlot.Select(k => k.Name).ToList(),
                        QuestName = taskName,
                        Type = BringItemType.Key
                    });
                }
            }

            // For mark objectives: use MarkerItem field (e.g., MS2000, etags, etc.)
            // For plantItem objectives: use Item field (e.g., Iskra ration pack, etc.)
            if (obj.Type == "mark")
            {
                if (obj.MarkerItem != null && !stash.Owns(obj.MarkerItem.Id))
                {
                    items.Add(new BringItem { Alternatives = [obj.MarkerItem.Name], QuestName = taskName, Type = BringItemType.QuestItem });
                }
                else if (obj.MarkerItem == null)
                {
                    // Fallback: some mark objectives might not have MarkerItem set
                    items.Add(new BringItem { Alternatives = ["MS2000 Marker"], QuestName = taskName, Type = BringItemType.QuestItem });
                }
            }
            else if (obj.Type == "plantItem")
            {
                // PlantItem objectives store the item to plant in Item field
                if (obj.Item != null && !stash.Owns(obj.Item.Id))
                {
                    items.Add(new BringItem { Alternatives = [obj.Item.Name], QuestName = taskName, Type = BringItemType.QuestItem });
                }
            }

            // QuestItem - only include for types where you bring the item IN (not find in raid)
            // INCLUDE: giveQuestItem, plant (hand over or place)
            // EXCLUDE: findQuestItem (must find in raid)
            // EXCLUDE: plantItem (handled above with QuestItem check)
            if (obj.QuestItem != null && !stash.Owns(obj.QuestItem.Id) && obj.Type != "plantItem")
            {
                var include = obj.Type switch
                {
                    "giveQuestItem" => true,
                    "plant" => true,  // placing quest items
                    "giveItem" => true,
                    _ => false
                };

                if (include)
                {
                    items.Add(new BringItem
                    {
                        Alternatives = [obj.QuestItem.Name],
                        QuestName = taskName,
                        Type = BringItemType.QuestItem
                    });
                }
            }

            // DO NOT include objective.Item (FIR items are raid objectives, not bring items)
        }

        // Aggregate: same item+quest combination summed by Count
        return items
            .GroupBy(i => (string.Join("|", i.Alternatives), i.QuestName))
            .Select(g => new BringItem
            {
                Alternatives = g.First().Alternatives,
                QuestName = g.First().QuestName,
                Type = g.First().Type,
                Count = g.Sum(x => x.Count)
            })
            .ToList();
    }

    /// <summary>
    /// Builds aggregated bring list at map level from all missions.
    /// Aggregates by item name across all missions, summing counts.
    /// </summary>
    private static List<BringItem> BuildFilteredBringList(List<MissionPlan> missions)
    {
        return missions
            .SelectMany(m => m.BringItems)
            .GroupBy(i => string.Join("|", i.Alternatives))
            .Select(g => new BringItem
            {
                Alternatives = g.First().Alternatives,
                QuestName = g.First().QuestName,
                Type = g.First().Type,
                Count = g.Sum(x => x.Count)
            })
            .OrderByDescending(i => i.Count)
            .ToList();
    }

    /// <summary>
    /// Gets quests that will be unlocked by completing quests on this map.
    /// For each unlocked quest, finds the first map from its objectives (or "Any" if none).
    /// </summary>
    private static List<UnlockedQuest> GetUnlockedQuestsForMap(
        HashSet<string> completableQuestIds,
        FrozenDictionary<string, TaskElement> taskData)
    {
        var unlocked = new List<UnlockedQuest>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (completableQuestIds.Count == 0)
            return unlocked;

        foreach (var task in taskData.Values)
        {
            if (task.TaskRequirements == null)
                continue;

            bool wouldBeUnlocked = task.TaskRequirements.Any(req =>
                req.Task?.Id != null &&
                completableQuestIds.Contains(req.Task.Id) &&
                req.Status != null &&
                req.Status.Contains("complete", StringComparer.OrdinalIgnoreCase));

            if (!wouldBeUnlocked)
                continue;

            if (seen.Contains(task.Id))
                continue;
            seen.Add(task.Id);

            var mapName = "Any";
            if (task.Objectives != null)
            {
                var firstObjective = task.Objectives.FirstOrDefault(obj => obj.Maps != null && obj.Maps.Count > 0);
                if (firstObjective != null && firstObjective.Maps != null && firstObjective.Maps.Count > 0)
                    mapName = firstObjective.Maps[0].Name;
            }

            unlocked.Add(new UnlockedQuest
            {
                QuestName = task.Name,
                MapName = mapName
            });
        }

        return unlocked;
    }

    /// <summary>
    /// Builds missions list for quests whose completable objectives have no map attribution.
    /// These appear in the "All Maps" section at the bottom of the Quest Planner.
    /// Applies findQuestItem/giveQuestItem pairing filter and excludes FIR pair objectives.
    /// </summary>
    private static List<MissionPlan> BuildAllMapsMissions(
        List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completableObjectives,
        IReadOnlyList<QuestData> quests,
        IStashFilter stash)
    {
        var completedByQuestId = quests.ToDictionary(q => q.Id, q => q.CompletedConditions, StringComparer.OrdinalIgnoreCase);
        var countersByQuestId = quests.ToDictionary(q => q.Id, q => q.ConditionCounters, StringComparer.OrdinalIgnoreCase);

        // Build task reference dictionary for accessing ALL objectives
        var taskRefById2 = new Dictionary<string, TaskElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (task, obj) in completableObjectives)
        {
            if (!taskRefById2.ContainsKey(task.Id))
                taskRefById2[task.Id] = task;
        }

        var objectivesByTask = new Dictionary<string, (string Name, List<TaskElement.ObjectiveElement> Objectives)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (task, obj) in completableObjectives)
        {
            // Objective has no map if obj.Maps is null/empty — regardless of quest-level map.
            // Quest-level map only overrides objectives that HAVE map data (sub-location fix).
            // Mapless objectives (trader handoffs, etc.) always go to All Maps.
            if (obj.Maps != null && obj.Maps.Count > 0)
                continue;

            if (!objectivesByTask.TryGetValue(task.Id, out var entry))
            {
                entry = (task.Name, new List<TaskElement.ObjectiveElement>());
                objectivesByTask[task.Id] = entry;
            }
            entry.Objectives.Add(obj);
        }

        var missions = new List<MissionPlan>();
        foreach (var (taskId, (taskName, objectives)) in objectivesByTask)
        {
            // Build findQuestItem pairing lookup
            var taskRef2 = taskRefById2.GetValueOrDefault(taskId);
            var allObjs2 = taskRef2?.Objectives ?? new List<TaskElement.ObjectiveElement>();
            var findLookup2 = allObjs2
                .Where(o => o.Type == "findQuestItem" && o.QuestItem != null)
                .ToDictionary(o => o.QuestItem!.Id, o => o.Id, StringComparer.OrdinalIgnoreCase);

            // Build FIR pair IDs to exclude from All Maps objective list
            // (FIR pairs go to the FirItems category instead)
            var firPairObjectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var firFindObjs = allObjs2.Where(o => o.Type == "findItem" && o.FoundInRaid && o.Item != null).ToList();
            var firGiveObjs = allObjs2.Where(o => o.Type == "giveItem" && o.Item != null).ToList();
            foreach (var findObj in firFindObjs)
            {
                var matchingGive = firGiveObjs.FirstOrDefault(g =>
                    string.Equals(g.Item!.Id, findObj.Item!.Id, StringComparison.OrdinalIgnoreCase));
                if (matchingGive != null)
                {
                    firPairObjectiveIds.Add(findObj.Id);
                    firPairObjectiveIds.Add(matchingGive.Id);
                }
            }

            var completedSet2 = completedByQuestId.GetValueOrDefault(taskId) ?? new HashSet<string>();
            var questCounters2 = countersByQuestId.GetValueOrDefault(taskId);

            var filteredObjectives2 = new List<ObjectiveInfo>();
            foreach (var o in objectives.Where(o => !completedSet2.Contains(o.Id)))
            {
                // Skip FIR pair objectives — they go in the FIR category
                if (firPairObjectiveIds.Contains(o.Id))
                    continue;

                if (o.Type == "giveQuestItem" && o.QuestItem != null)
                {
                    if (findLookup2.TryGetValue(o.QuestItem.Id, out var findObjId2))
                    {
                        if (!completedSet2.Contains(findObjId2))
                            continue;
                    }
                }
                filteredObjectives2.Add(new ObjectiveInfo(
                    o.Id,
                    o.Description,
                    false,
                    questCounters2 != null && questCounters2.TryGetValue(o.Id, out var cnt2) ? cnt2 : 0,
                    o.Count,
                    o.Type
                ));
            }

            // Only add mission if there are objectives remaining (skip pure FIR quests)
            if (filteredObjectives2.Count > 0)
            {
                missions.Add(new MissionPlan
                {
                    MissionName = taskName,
                    Objectives = filteredObjectives2,
                    BringItems = BuildBringListForMission(objectives, taskName, stash)
                });
            }
        }

        return missions;
    }

    /// <summary>
    /// Builds the "Find in raid" category items.
    /// Collapses findItem (foundInRaid=true) + giveItem pairs (same item.Id) into FirItemInfo rows.
    /// Uses ConditionCounters for live CurrentCount from memory.
    /// </summary>
    private static List<FirItemInfo> BuildFirItems(
        IReadOnlyList<QuestData> quests,
        FrozenDictionary<string, TaskElement> taskData)
    {
        var result = new List<FirItemInfo>();
        var countersByQuestId = quests.ToDictionary(q => q.Id, q => q.ConditionCounters, StringComparer.OrdinalIgnoreCase);

        foreach (var quest in quests)
        {
            if (!taskData.TryGetValue(quest.Id, out var task)) continue;
            if (task.Objectives == null || task.Objectives.Count == 0) continue;

            var findObjs = task.Objectives
                .Where(o => o.Type == "findItem" && o.FoundInRaid && o.Item != null)
                .ToList();
            var giveObjs = task.Objectives
                .Where(o => o.Type == "giveItem" && o.Item != null)
                .ToList();

            var questCounters = countersByQuestId.GetValueOrDefault(quest.Id);

            foreach (var findObj in findObjs)
            {
                // Check for a matching giveItem (same Item.Id)
                var matchingGive = giveObjs.FirstOrDefault(g =>
                    string.Equals(g.Item!.Id, findObj.Item!.Id, StringComparison.OrdinalIgnoreCase));
                if (matchingGive == null) continue;

                // Skip if giveItem is already completed (hand-over done)
                if (quest.CompletedConditions.Contains(matchingGive.Id)) continue;

                var currentCount = questCounters != null && questCounters.TryGetValue(matchingGive.Id, out var cnt) ? cnt : 0;
                var shortName = findObj.Item!.ShortName ?? findObj.Item.Name ?? "item";

                result.Add(new FirItemInfo(
                    QuestName: task.Name,
                    ItemShortName: shortName,
                    CurrentCount: currentCount,
                    TargetCount: findObj.Count > 0 ? findObj.Count : 1
                ));
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the "Hand over items" banner data.
    /// Detects Started quests where ALL incomplete objectives are of type giveQuestItem.
    /// These are quests where the player has the item but hasn't traded it in yet.
    /// This is distinct from AvailableForFinish (EQuestStatus=3): here the quest is still Started (status=2)
    /// but only hand-over steps remain.
    /// </summary>
    private static List<HandOverItemInfo> BuildHandOverItems(
        IReadOnlyList<QuestData> quests,
        FrozenDictionary<string, TaskElement> taskData)
    {
        var result = new List<HandOverItemInfo>();

        foreach (var quest in quests)
        {
            if (!taskData.TryGetValue(quest.Id, out var task)) continue;
            if (task.Objectives == null || task.Objectives.Count == 0) continue;

            var incompleteObjectives = task.Objectives
                .Where(o => !quest.CompletedConditions.Contains(o.Id))
                .ToList();

            if (incompleteObjectives.Count == 0) continue; // All done — AvailableForFinish handles this

            bool allAreGiveQuestItem = incompleteObjectives.All(o => o.Type == "giveQuestItem");
            if (!allAreGiveQuestItem) continue;

            foreach (var giveObj in incompleteObjectives)
            {
                var shortName = giveObj.QuestItem?.ShortName
                             ?? giveObj.QuestItem?.Name
                             ?? "item";

                result.Add(new HandOverItemInfo(
                    QuestName: task.Name,
                    ItemShortName: shortName
                ));
            }
        }

        return result;
    }
}
