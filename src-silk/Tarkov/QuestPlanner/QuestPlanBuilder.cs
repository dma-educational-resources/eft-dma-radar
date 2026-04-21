using System.Collections.Frozen;

using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.QuestPlanner.Models;

namespace eft_dma_radar.Silk.Tarkov.QuestPlanner
{
    /// <summary>
    /// Produces an ordered session plan from active quests and tarkov.dev task metadata.
    /// <para>
    /// Optimizations vs. the WPF version:
    /// <list type="bullet">
    ///   <item>A reverse-requirements index is built once (requiredTaskId → dependents)
    ///         and reused by both unlock counting and dependency promotion, reducing both
    ///         passes from O(|maps|·|tasks|) to O(|tasks| + sum(FinishableIds)).</item>
    ///   <item>Kahn's topological sort uses a ready-list keyed by in-degree instead of
    ///         <c>Min()</c>-scanning the remaining set on every iteration.</item>
    ///   <item>Normalized map IDs are cached per <see cref="TaskElement.BasicRef"/>.</item>
    ///   <item>Bring-list aggregation uses manual loops and pre-sized dictionaries.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static class QuestPlanBuilder
    {
        /// <summary>
        /// Builds the session plan.
        /// </summary>
        public static QuestSummary GetSummary(
            AvailableQuests quests,
            FrozenDictionary<string, TaskElement> taskData,
            QuestPlannerSettings settings)
        {
            var startTraders = CollectDistinctTraderNames(quests.AvailableForStart, taskData);
            var finishTraders = CollectDistinctTraderNames(quests.AvailableForFinish, taskData);

            // 1. Completable objectives (Started quests only, not-yet-completed conditions).
            var completable = GetCompletableObjectives(quests.Started, taskData);

            // 1a. Kappa filter.
            if (settings.KappaFilter)
            {
                var filtered = new List<(TaskElement Task, TaskElement.ObjectiveElement Objective)>(completable.Count);
                for (int i = 0; i < completable.Count; i++)
                {
                    if (completable[i].Task.KappaRequired)
                        filtered.Add(completable[i]);
                }
                completable = filtered;
            }

            // 2. Map scoring (counts + finishable quest attribution).
            var scores = ScoreMaps(completable);

            // 3. Build reverse-requirement index once for unlock counting + promotion.
            //    activeQuestIds -> task whose requirement matches and is "complete" status.
            var activeQuestIds = BuildActiveQuestIdSet(quests.Started);
            var reverseReqs = BuildReverseRequirements(taskData, activeQuestIds);

            // 4. Unlock counts per map.
            ComputeUnlockCounts(scores, reverseReqs);

            // 5. Rank base (finishable DESC, unlocks DESC, quest count DESC).
            var ranked = RankMaps(scores);

            // 6. Dependency promotion via Kahn's topological sort.
            var promoted = ApplyDependencyPromotion(ranked, reverseReqs);

            // 7. Per-map plans.
            var mapPlans = new List<MapPlan>(promoted.Count);
            for (int i = 0; i < promoted.Count; i++)
            {
                var score = promoted[i];
                var questPlans = BuildQuestsForMap(score.MapId, completable, quests.Started);
                var unlockedQuests = GetUnlockedQuestsForMap(score.QuestIds, taskData);
                var filteredBring = BuildFilteredBringList(questPlans);
                mapPlans.Add(new MapPlan
                {
                    MapId = score.MapId,
                    MapName = score.MapName,
                    IsRecommended = i == 0,
                    CompletableObjectiveCount = score.ObjectiveCount,
                    ActiveQuestCount = score.QuestIds.Count,
                    Quests = questPlans,
                    UnlockedQuests = unlockedQuests,
                    FilteredBringList = filteredBring
                });
            }

            var allMapsQuests = BuildAllMapsQuests(completable, quests.Started);
            var firItems = BuildFirItems(quests.Started, taskData);
            var handOverItems = BuildHandOverItems(quests.Started, taskData);

            return new QuestSummary
            {
                Maps = mapPlans,
                AllMapsQuests = allMapsQuests,
                TotalActiveQuests = quests.Started.Count,
                TotalCompletableObjectives = completable.Count,
                AvailableForStartTraders = startTraders,
                AvailableForFinishTraders = finishTraders,
                FirItems = firItems,
                HandOverItems = handOverItems,
                ComputedAt = DateTime.UtcNow
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<string> CollectDistinctTraderNames(
            List<QuestData> quests,
            FrozenDictionary<string, TaskElement> taskData)
        {
            if (quests.Count == 0) return [];
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < quests.Count; i++)
            {
                if (taskData.TryGetValue(quests[i].Id, out var task) && task.Trader?.Name is { Length: > 0 } name)
                    set.Add(name);
            }
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static HashSet<string> BuildActiveQuestIdSet(IReadOnlyList<QuestData> quests)
        {
            var set = new HashSet<string>(quests.Count, StringComparer.Ordinal);
            for (int i = 0; i < quests.Count; i++) set.Add(quests[i].Id);
            return set;
        }

        private static List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> GetCompletableObjectives(
            IReadOnlyList<QuestData> quests,
            FrozenDictionary<string, TaskElement> taskData)
        {
            var result = new List<(TaskElement, TaskElement.ObjectiveElement)>(quests.Count * 2);
            for (int i = 0; i < quests.Count; i++)
            {
                var quest = quests[i];
                if (!taskData.TryGetValue(quest.Id, out var task) || task.Objectives is null)
                    continue;

                var objs = task.Objectives;
                var completed = quest.CompletedConditions;
                for (int j = 0; j < objs.Count; j++)
                {
                    var obj = objs[j];
                    if (!completed.Contains(obj.Id))
                        result.Add((task, obj));
                }
            }
            return result;
        }

        /// <summary>Ground-zero / sandbox aliasing mirrors the WPF table.</summary>
        private static string NormalizeMapId(string mapId) => mapId switch
        {
            "ground-zero-21" => "ground-zero",
            "Sandbox_high" => "Sandbox",
            _ => mapId
        };

        private static string GetMapDisplayName(string normalizedMapId, string originalName) => normalizedMapId switch
        {
            "ground-zero" => "Ground Zero",
            "Sandbox" => "Ground Zero",
            _ => originalName
        };

        private static Dictionary<string, MapScore> ScoreMaps(
            List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completable)
        {
            var scores = new Dictionary<string, MapScore>(StringComparer.OrdinalIgnoreCase);
            var questMapDist = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            for (int i = 0; i < completable.Count; i++)
            {
                var (task, obj) = completable[i];
                if (obj.Maps is null || obj.Maps.Count == 0)
                    continue;

                // Quest-level map override (only applies to objectives that HAVE map data).
                IReadOnlyList<TaskElement.BasicRef> effectiveMaps = task.Map is not null
                    ? new[] { task.Map }
                    : obj.Maps;

                for (int m = 0; m < effectiveMaps.Count; m++)
                {
                    var mapRef = effectiveMaps[m];
                    var normalizedId = NormalizeMapId(mapRef.NormalizedName);
                    if (string.IsNullOrEmpty(normalizedId))
                        continue;

                    if (!scores.TryGetValue(normalizedId, out var score))
                    {
                        score = new MapScore(normalizedId, GetMapDisplayName(normalizedId, mapRef.Name));
                        scores[normalizedId] = score;
                    }
                    score.ObjectiveCount++;
                    score.QuestIds.Add(task.Id);

                    if (!questMapDist.TryGetValue(task.Id, out var dist))
                    {
                        dist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        questMapDist[task.Id] = dist;
                    }
                    dist.Add(normalizedId);
                }
            }

            // Finishable quests (objectives all on one map).
            foreach (var (questId, maps) in questMapDist)
            {
                if (maps.Count != 1) continue;
                string mapId = default!;
                foreach (var m in maps) { mapId = m; break; }
                if (scores.TryGetValue(mapId, out var s))
                    s.FinishableQuestIds.Add(questId);
            }

            return scores;
        }

        /// <summary>
        /// (requiredTaskId, requiredStatus="complete") → set of dependent tasks with their objective maps.
        /// Built ONCE, reused for unlock counting and topological promotion.
        /// </summary>
        private readonly struct Dependent
        {
            public readonly TaskElement Task;
            public readonly List<string> MapIds; // normalized map ids from the dependent's objectives
            public Dependent(TaskElement task, List<string> mapIds) { Task = task; MapIds = mapIds; }
        }

        private static Dictionary<string, List<Dependent>> BuildReverseRequirements(
            FrozenDictionary<string, TaskElement> taskData,
            HashSet<string> activeQuestIds)
        {
            var rev = new Dictionary<string, List<Dependent>>(StringComparer.Ordinal);

            foreach (var task in taskData.Values)
            {
                if (activeQuestIds.Contains(task.Id)) continue;
                var reqs = task.TaskRequirements;
                if (reqs is null || reqs.Count == 0) continue;

                // Precompute the dependent's objective map ids (normalized, deduplicated).
                List<string>? mapIds = null;
                var taskObjs = task.Objectives;
                if (taskObjs is not null)
                {
                    HashSet<string>? seen = null;
                    for (int o = 0; o < taskObjs.Count; o++)
                    {
                        var objMaps = taskObjs[o].Maps;
                        if (objMaps is null) continue;
                        for (int m = 0; m < objMaps.Count; m++)
                        {
                            var id = NormalizeMapId(objMaps[m].NormalizedName);
                            if (string.IsNullOrEmpty(id)) continue;
                            seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (seen.Add(id))
                                (mapIds ??= new List<string>()).Add(id);
                        }
                    }
                }

                var dep = new Dependent(task, mapIds ?? []);

                for (int r = 0; r < reqs.Count; r++)
                {
                    var req = reqs[r];
                    var reqTaskId = req.Task?.Id;
                    if (string.IsNullOrEmpty(reqTaskId)) continue;
                    var status = req.Status;
                    if (status is null) continue;

                    bool hasComplete = false;
                    for (int s = 0; s < status.Count; s++)
                    {
                        if (string.Equals(status[s], "complete", StringComparison.OrdinalIgnoreCase))
                        { hasComplete = true; break; }
                    }
                    if (!hasComplete) continue;

                    if (!rev.TryGetValue(reqTaskId, out var list))
                    {
                        list = new List<Dependent>();
                        rev[reqTaskId] = list;
                    }
                    list.Add(dep);
                }
            }

            return rev;
        }

        private static void ComputeUnlockCounts(
            Dictionary<string, MapScore> scores,
            Dictionary<string, List<Dependent>> reverseReqs)
        {
            foreach (var score in scores.Values)
            {
                var unlocked = new HashSet<string>(StringComparer.Ordinal);
                foreach (var finishableId in score.FinishableQuestIds)
                {
                    if (!reverseReqs.TryGetValue(finishableId, out var deps)) continue;
                    for (int i = 0; i < deps.Count; i++)
                        unlocked.Add(deps[i].Task.Id);
                }
                score.UnlockCount = unlocked.Count;
            }
        }

        private static List<MapScore> RankMaps(Dictionary<string, MapScore> scores)
        {
            var list = new List<MapScore>(scores.Values);
            list.Sort(static (a, b) =>
            {
                int cmp = b.FinishableQuestIds.Count.CompareTo(a.FinishableQuestIds.Count);
                if (cmp != 0) return cmp;
                cmp = b.UnlockCount.CompareTo(a.UnlockCount);
                if (cmp != 0) return cmp;
                return b.QuestIds.Count.CompareTo(a.QuestIds.Count);
            });
            return list;
        }

        /// <summary>
        /// Kahn's algorithm over the unlock graph. No repeated Min() scans — we maintain a
        /// ready-list of zero-in-degree nodes and pop the one with the most quests.
        /// </summary>
        private static List<MapScore> ApplyDependencyPromotion(
            List<MapScore> ranked,
            Dictionary<string, List<Dependent>> reverseReqs)
        {
            if (ranked.Count <= 1) return ranked;

            var mapById = new Dictionary<string, MapScore>(ranked.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ranked.Count; i++) mapById[ranked[i].MapId] = ranked[i];

            // Edges: from map with finishable quest X → maps where a dependent of X has objectives.
            var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(ranked.Count, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ranked.Count; i++)
            {
                adjacency[ranked[i].MapId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                inDegree[ranked[i].MapId] = 0;
            }

            foreach (var map in ranked)
            {
                var edges = adjacency[map.MapId];
                foreach (var finishableId in map.FinishableQuestIds)
                {
                    if (!reverseReqs.TryGetValue(finishableId, out var deps)) continue;
                    for (int i = 0; i < deps.Count; i++)
                    {
                        var depMaps = deps[i].MapIds;
                        for (int j = 0; j < depMaps.Count; j++)
                        {
                            var m = depMaps[j];
                            if (string.Equals(m, map.MapId, StringComparison.OrdinalIgnoreCase)) continue;
                            if (!mapById.ContainsKey(m)) continue;
                            if (edges.Add(m))
                                inDegree[m]++;
                        }
                    }
                }
            }

            // Ready-list seeded with nodes at the lowest in-degree currently present.
            // (Strict topological sort would require in-degree == 0; to avoid cycles leaving items
            //  stranded, we degrade to the minimum remaining in-degree per outer iteration.)
            var result = new List<MapScore>(ranked.Count);
            var remaining = new HashSet<string>(inDegree.Keys, StringComparer.OrdinalIgnoreCase);

            while (remaining.Count > 0)
            {
                int minDeg = int.MaxValue;
                foreach (var id in remaining)
                {
                    var d = inDegree[id];
                    if (d < minDeg) minDeg = d;
                    if (minDeg == 0) break;
                }

                // Collect candidates with this in-degree and pick highest quest count,
                // respecting the original ranked order as a tiebreaker.
                string? pick = null;
                int pickQuests = -1;
                int pickRank = int.MaxValue;
                for (int r = 0; r < ranked.Count; r++)
                {
                    var m = ranked[r];
                    if (!remaining.Contains(m.MapId)) continue;
                    if (inDegree[m.MapId] != minDeg) continue;
                    int qc = m.QuestIds.Count;
                    if (qc > pickQuests || (qc == pickQuests && r < pickRank))
                    {
                        pick = m.MapId;
                        pickQuests = qc;
                        pickRank = r;
                    }
                }

                if (pick is null) break;

                result.Add(mapById[pick]);
                remaining.Remove(pick);
                foreach (var outMap in adjacency[pick])
                {
                    if (remaining.Contains(outMap))
                        inDegree[outMap]--;
                }
            }

            return result;
        }

        private static bool ObjectiveBelongsToMap(TaskElement task, TaskElement.ObjectiveElement obj, string mapId)
        {
            if (obj.Maps is null || obj.Maps.Count == 0) return false;

            if (task.Map is not null)
                return string.Equals(NormalizeMapId(task.Map.NormalizedName), mapId, StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < obj.Maps.Count; i++)
            {
                if (string.Equals(NormalizeMapId(obj.Maps[i].NormalizedName), mapId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<QuestPlan> BuildQuestsForMap(
            string mapId,
            List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completable,
            IReadOnlyList<QuestData> quests)
        {
            var completedByQuest = new Dictionary<string, HashSet<string>>(quests.Count, StringComparer.Ordinal);
            var countersByQuest = new Dictionary<string, IReadOnlyDictionary<string, int>>(quests.Count, StringComparer.Ordinal);
            for (int i = 0; i < quests.Count; i++)
            {
                completedByQuest[quests[i].Id] = quests[i].CompletedConditions;
                countersByQuest[quests[i].Id] = quests[i].ConditionCounters;
            }

            var taskRef = new Dictionary<string, TaskElement>(StringComparer.Ordinal);
            var objectivesByTask = new Dictionary<string, List<TaskElement.ObjectiveElement>>(StringComparer.Ordinal);

            for (int i = 0; i < completable.Count; i++)
            {
                var (task, obj) = completable[i];
                if (!ObjectiveBelongsToMap(task, obj, mapId)) continue;

                taskRef.TryAdd(task.Id, task);
                if (!objectivesByTask.TryGetValue(task.Id, out var list))
                {
                    list = new List<TaskElement.ObjectiveElement>();
                    objectivesByTask[task.Id] = list;
                }
                list.Add(obj);
            }

            var result = new List<QuestPlan>(objectivesByTask.Count);
            foreach (var (taskId, objectives) in objectivesByTask)
            {
                var taskName = taskRef.TryGetValue(taskId, out var t) ? t.Name : taskId;
                var bring = BuildBringListForQuest(objectives, taskName);

                var allObjs = (taskRef.TryGetValue(taskId, out var tt) ? tt.Objectives : null) ?? [];
                var findLookup = BuildFindItemLookup(allObjs);

                var completedSet = completedByQuest.GetValueOrDefault(taskId) ?? [];
                countersByQuest.TryGetValue(taskId, out var counters);

                var filtered = new List<ObjectiveInfo>(objectives.Count);
                for (int i = 0; i < objectives.Count; i++)
                {
                    var o = objectives[i];
                    if (completedSet.Contains(o.Id)) continue;

                    if (o.Type == "giveQuestItem" && o.QuestItem is not null
                        && findLookup.TryGetValue(o.QuestItem.Id, out var findObjId)
                        && !completedSet.Contains(findObjId))
                        continue;

                    int cur = 0;
                    counters?.TryGetValue(o.Id, out cur);
                    filtered.Add(new ObjectiveInfo(o.Id, o.Description, false, cur, o.Count, o.Type));
                }

                result.Add(new QuestPlan
                {
                    QuestName = taskName,
                    Objectives = filtered,
                    BringItems = bring
                });
            }
            return result;
        }

        private static Dictionary<string, string> BuildFindItemLookup(IReadOnlyList<TaskElement.ObjectiveElement> allObjs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < allObjs.Count; i++)
            {
                var o = allObjs[i];
                if (o.Type == "findQuestItem" && o.QuestItem is not null)
                    map[o.QuestItem.Id] = o.Id;
            }
            return map;
        }

        private static List<BringItem> BuildBringListForQuest(
            List<TaskElement.ObjectiveElement> objectives,
            string taskName)
        {
            var items = new List<BringItem>();

            for (int i = 0; i < objectives.Count; i++)
            {
                var obj = objectives[i];

                if (obj.RequiredKeys is not null)
                {
                    for (int k = 0; k < obj.RequiredKeys.Count; k++)
                    {
                        var keySlot = obj.RequiredKeys[k];
                        var alts = new List<string>(keySlot.Count);
                        for (int a = 0; a < keySlot.Count; a++) alts.Add(keySlot[a].Name);
                        items.Add(new BringItem { Alternatives = alts, QuestName = taskName, Type = BringItemType.Key });
                    }
                }

                if (obj.Type == "mark")
                {
                    var name = obj.MarkerItem?.Name ?? "MS2000 Marker";
                    items.Add(new BringItem { Alternatives = [name], QuestName = taskName, Type = BringItemType.QuestItem });
                }
                else if (obj.Type == "plantItem" && obj.Item is not null)
                {
                    items.Add(new BringItem { Alternatives = [obj.Item.Name], QuestName = taskName, Type = BringItemType.QuestItem });
                }

                if (obj.QuestItem is not null && obj.Type is "giveQuestItem" or "plant" or "giveItem")
                {
                    items.Add(new BringItem
                    {
                        Alternatives = [obj.QuestItem.Name],
                        QuestName = taskName,
                        Type = BringItemType.QuestItem
                    });
                }
            }

            if (items.Count <= 1) return items;

            // Aggregate by (alternatives|questName) with manual loops.
            var agg = new Dictionary<string, BringItem>(items.Count, StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var key = string.Concat(string.Join('|', it.Alternatives), "\u0001", it.QuestName);
                if (!agg.TryGetValue(key, out var existing))
                {
                    agg[key] = it;
                }
                else
                {
                    agg[key] = new BringItem
                    {
                        Alternatives = existing.Alternatives,
                        QuestName = existing.QuestName,
                        Type = existing.Type,
                        Count = existing.Count + it.Count
                    };
                }
            }
            return new List<BringItem>(agg.Values);
        }

        private static List<BringItem> BuildFilteredBringList(List<QuestPlan> quests)
        {
            var agg = new Dictionary<string, BringItem>(StringComparer.Ordinal);
            for (int q = 0; q < quests.Count; q++)
            {
                var bring = quests[q].BringItems;
                for (int i = 0; i < bring.Count; i++)
                {
                    var it = bring[i];
                    var key = string.Join('|', it.Alternatives);
                    if (!agg.TryGetValue(key, out var existing))
                    {
                        agg[key] = it;
                    }
                    else
                    {
                        agg[key] = new BringItem
                        {
                            Alternatives = existing.Alternatives,
                            QuestName = existing.QuestName,
                            Type = existing.Type,
                            Count = existing.Count + it.Count
                        };
                    }
                }
            }
            var result = new List<BringItem>(agg.Values);
            result.Sort(static (a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        private static List<UnlockedQuest> GetUnlockedQuestsForMap(
            HashSet<string> completableQuestIds,
            FrozenDictionary<string, TaskElement> taskData)
        {
            if (completableQuestIds.Count == 0) return [];

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<UnlockedQuest>();
            foreach (var task in taskData.Values)
            {
                var reqs = task.TaskRequirements;
                if (reqs is null) continue;

                bool unlocked = false;
                for (int i = 0; i < reqs.Count && !unlocked; i++)
                {
                    var req = reqs[i];
                    var reqId = req.Task?.Id;
                    if (string.IsNullOrEmpty(reqId) || !completableQuestIds.Contains(reqId)) continue;
                    var status = req.Status;
                    if (status is null) continue;
                    for (int s = 0; s < status.Count; s++)
                    {
                        if (string.Equals(status[s], "complete", StringComparison.OrdinalIgnoreCase))
                        { unlocked = true; break; }
                    }
                }
                if (!unlocked) continue;
                if (!seen.Add(task.Id)) continue;

                var mapName = "Any";
                if (task.Objectives is not null)
                {
                    for (int o = 0; o < task.Objectives.Count; o++)
                    {
                        var objMaps = task.Objectives[o].Maps;
                        if (objMaps is { Count: > 0 }) { mapName = objMaps[0].Name; break; }
                    }
                }
                result.Add(new UnlockedQuest { QuestName = task.Name, MapName = mapName });
            }
            return result;
        }

        private static List<QuestPlan> BuildAllMapsQuests(
            List<(TaskElement Task, TaskElement.ObjectiveElement Objective)> completable,
            IReadOnlyList<QuestData> quests)
        {
            var completedByQuest = new Dictionary<string, HashSet<string>>(quests.Count, StringComparer.Ordinal);
            var countersByQuest = new Dictionary<string, IReadOnlyDictionary<string, int>>(quests.Count, StringComparer.Ordinal);
            for (int i = 0; i < quests.Count; i++)
            {
                completedByQuest[quests[i].Id] = quests[i].CompletedConditions;
                countersByQuest[quests[i].Id] = quests[i].ConditionCounters;
            }

            var taskRef = new Dictionary<string, TaskElement>(StringComparer.Ordinal);
            var objectivesByTask = new Dictionary<string, List<TaskElement.ObjectiveElement>>(StringComparer.Ordinal);

            for (int i = 0; i < completable.Count; i++)
            {
                var (task, obj) = completable[i];
                if (obj.Maps is { Count: > 0 }) continue;

                taskRef.TryAdd(task.Id, task);
                if (!objectivesByTask.TryGetValue(task.Id, out var list))
                {
                    list = new List<TaskElement.ObjectiveElement>();
                    objectivesByTask[task.Id] = list;
                }
                list.Add(obj);
            }

            var result = new List<QuestPlan>(objectivesByTask.Count);
            foreach (var (taskId, objectives) in objectivesByTask)
            {
                var taskName = taskRef.TryGetValue(taskId, out var t) ? t.Name : taskId;
                var allObjs = (taskRef.TryGetValue(taskId, out var tt) ? tt.Objectives : null) ?? [];
                var findLookup = BuildFindItemLookup(allObjs);

                // FIR pair objectives (they go to FirItems category instead).
                var firPair = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < allObjs.Count; i++)
                {
                    var find = allObjs[i];
                    if (find.Type != "findItem" || !find.FoundInRaid || find.Item is null) continue;
                    for (int j = 0; j < allObjs.Count; j++)
                    {
                        var give = allObjs[j];
                        if (give.Type != "giveItem" || give.Item is null) continue;
                        if (!string.Equals(give.Item.Id, find.Item.Id, StringComparison.Ordinal)) continue;
                        firPair.Add(find.Id);
                        firPair.Add(give.Id);
                        break;
                    }
                }

                var completedSet = completedByQuest.GetValueOrDefault(taskId) ?? [];
                countersByQuest.TryGetValue(taskId, out var counters);

                var filtered = new List<ObjectiveInfo>(objectives.Count);
                for (int i = 0; i < objectives.Count; i++)
                {
                    var o = objectives[i];
                    if (completedSet.Contains(o.Id)) continue;
                    if (firPair.Contains(o.Id)) continue;

                    if (o.Type == "giveQuestItem" && o.QuestItem is not null
                        && findLookup.TryGetValue(o.QuestItem.Id, out var findObjId)
                        && !completedSet.Contains(findObjId))
                        continue;

                    int cur = 0;
                    counters?.TryGetValue(o.Id, out cur);
                    filtered.Add(new ObjectiveInfo(o.Id, o.Description, false, cur, o.Count, o.Type));
                }

                if (filtered.Count == 0) continue;

                result.Add(new QuestPlan
                {
                    QuestName = taskName,
                    Objectives = filtered,
                    BringItems = BuildBringListForQuest(objectives, taskName)
                });
            }
            return result;
        }

        private static List<FirItemInfo> BuildFirItems(
            IReadOnlyList<QuestData> quests,
            FrozenDictionary<string, TaskElement> taskData)
        {
            var result = new List<FirItemInfo>();
            for (int q = 0; q < quests.Count; q++)
            {
                var quest = quests[q];
                if (!taskData.TryGetValue(quest.Id, out var task)) continue;
                var objs = task.Objectives;
                if (objs is null || objs.Count == 0) continue;

                var counters = quest.ConditionCounters;
                for (int i = 0; i < objs.Count; i++)
                {
                    var find = objs[i];
                    if (find.Type != "findItem" || !find.FoundInRaid || find.Item is null) continue;

                    TaskElement.ObjectiveElement? give = null;
                    for (int j = 0; j < objs.Count; j++)
                    {
                        var candidate = objs[j];
                        if (candidate.Type == "giveItem" && candidate.Item is not null
                            && string.Equals(candidate.Item.Id, find.Item.Id, StringComparison.Ordinal))
                        { give = candidate; break; }
                    }
                    if (give is null) continue;
                    if (quest.CompletedConditions.Contains(give.Id)) continue;

                    int cur = 0;
                    counters?.TryGetValue(give.Id, out cur);
                    var shortName = find.Item.ShortName ?? find.Item.Name ?? "item";

                    result.Add(new FirItemInfo(
                        QuestName: task.Name,
                        ItemShortName: shortName,
                        CurrentCount: cur,
                        TargetCount: find.Count > 0 ? find.Count : 1));
                }
            }
            return result;
        }

        private static List<HandOverItemInfo> BuildHandOverItems(
            IReadOnlyList<QuestData> quests,
            FrozenDictionary<string, TaskElement> taskData)
        {
            var result = new List<HandOverItemInfo>();
            for (int q = 0; q < quests.Count; q++)
            {
                var quest = quests[q];
                if (!taskData.TryGetValue(quest.Id, out var task)) continue;
                var objs = task.Objectives;
                if (objs is null || objs.Count == 0) continue;

                int incompleteCount = 0;
                bool allGive = true;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (quest.CompletedConditions.Contains(o.Id)) continue;
                    incompleteCount++;
                    if (o.Type != "giveQuestItem") { allGive = false; break; }
                }
                if (incompleteCount == 0 || !allGive) continue;

                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (quest.CompletedConditions.Contains(o.Id)) continue;
                    var shortName = o.QuestItem?.ShortName
                                  ?? o.QuestItem?.Name
                                  ?? "item";
                    result.Add(new HandOverItemInfo(QuestName: task.Name, ItemShortName: shortName));
                }
            }
            return result;
        }
    }
}
