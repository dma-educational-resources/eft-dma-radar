using System.Runtime.CompilerServices;

using eft_dma_radar.Silk.Tarkov.GameWorld.Quests;
using eft_dma_radar.Silk.Tarkov.QuestPlanner.Models;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.QuestPlanner
{
    /// <summary>
    /// Reads quest planning state from the player profile. Groups quests by status
    /// (Started / AvailableForStart / AvailableForFinish) and resolves condition counters.
    /// </summary>
    internal static class QuestPlannerMemoryReader
    {
        // EQuestStatus values we care about
        private const int StatusAvailableForStart = 1;
        private const int StatusStarted = 2;
        private const int StatusAvailableForFinish = 3;

        private static int _lastStarted = -1;
        private static int _lastAvailStart = -1;
        private static int _lastAvailFinish = -1;

        public static AvailableQuests ReadAvailableQuests(ulong profile)
        {
            var result = new AvailableQuests();
            if (profile == 0)
                return result;

            try
            {
                // Shared across all quests from one profile read — avoid re-reading.
                var counters = ReadConditionCounters(profile);

                if (!Memory.TryReadPtr(profile + Offsets.Profile.QuestsData, out var questsDataPtr, false) || questsDataPtr == 0)
                    return result;

                if (!Memory.TryReadPtr(questsDataPtr + ManagedList.ItemsPtr, out var listItemsPtr, false))
                    return result;

                var listCount = Memory.ReadValue<int>(questsDataPtr + ManagedList.Count, false);
                if (listCount <= 0 || listCount > 500)
                    return result;

                for (int i = 0; i < listCount; i++)
                {
                    if (!Memory.TryReadPtr(
                            listItemsPtr + ManagedArray.FirstElement + (ulong)(i * ManagedArray.ElementSize),
                            out var qDataEntry, false))
                        continue;

                    if (!Memory.TryReadValue(qDataEntry + Offsets.QuestData.Status, out int qStatus, false))
                        continue;

                    if (qStatus != StatusStarted && qStatus != StatusAvailableForStart && qStatus != StatusAvailableForFinish)
                        continue;

                    if (!Memory.TryReadPtr(qDataEntry + Offsets.QuestData.Id, out var qIdPtr, false) || qIdPtr == 0)
                        continue;

                    var qId = Memory.ReadUnityString(qIdPtr);
                    if (string.IsNullOrEmpty(qId))
                        continue;

                    var completed = new HashSet<string>(StringComparer.Ordinal);
                    if (qStatus == StatusStarted
                        && Memory.TryReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions, out var hsPtr, false)
                        && hsPtr != 0)
                    {
                        MongoIdHashSetReader.Read(hsPtr, completed);
                    }

                    var qd = new QuestData
                    {
                        Id = qId,
                        CompletedConditions = completed,
                        ConditionCounters = counters
                    };

                    switch (qStatus)
                    {
                        case StatusStarted: result.Started.Add(qd); break;
                        case StatusAvailableForStart: result.AvailableForStart.Add(qd); break;
                        case StatusAvailableForFinish: result.AvailableForFinish.Add(qd); break;
                    }
                }

                if (result.Started.Count != _lastStarted
                    || result.AvailableForStart.Count != _lastAvailStart
                    || result.AvailableForFinish.Count != _lastAvailFinish)
                {
                    _lastStarted = result.Started.Count;
                    _lastAvailStart = result.AvailableForStart.Count;
                    _lastAvailFinish = result.AvailableForFinish.Count;
                    Log.WriteLine($"[QuestPlannerReader] Started={_lastStarted} AvailableForStart={_lastAvailStart} AvailableForFinish={_lastAvailFinish}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "qp_reader", TimeSpan.FromSeconds(30),
                    $"[QuestPlannerReader] Error reading quests: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Reads TaskConditionCounters (Profile + 0x90): Dictionary&lt;MongoID, TaskConditionCounter*&gt;.
        /// </summary>
        private static Dictionary<string, int> ReadConditionCounters(ulong profile)
        {
            var counters = new Dictionary<string, int>(StringComparer.Ordinal);
            if (profile == 0) return counters;

            try
            {
                if (!Memory.TryReadPtr(profile + Offsets.Profile.TaskConditionCounters, out var dictPtr, false) || dictPtr == 0)
                    return counters;

                using var dict = MemDictionary<Types.MongoID, ulong>.Get(dictPtr);
                foreach (var entry in dict)
                {
                    try
                    {
                        var stringIdPtr = entry.Key.StringID;
                        if (stringIdPtr < 0x10000000)
                            continue;

                        var condId = Memory.ReadUnityString(stringIdPtr);
                        if (string.IsNullOrEmpty(condId))
                            continue;

                        var counterPtr = entry.Value;
                        if (counterPtr == 0)
                            continue;

                        if (Memory.TryReadValue(counterPtr + Offsets.TaskConditionCounter.Value, out int value, false))
                            counters[condId] = value;
                    }
                    catch { /* skip bad entry */ }
                }
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "qp_counters", TimeSpan.FromSeconds(30),
                    $"[QuestPlannerReader] Error reading condition counters: {ex.Message}");
            }

            return counters;
        }
    }
}
