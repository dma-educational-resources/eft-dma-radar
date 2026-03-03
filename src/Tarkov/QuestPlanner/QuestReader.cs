using static eft_dma_radar.Tarkov.MemoryInterface;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using SDK;
using System.Text;

namespace eft_dma_radar.Tarkov.QuestPlanner
{
    /// <summary>
    /// Container for quests grouped by their availability status.
    /// </summary>
    public sealed class AvailableQuests
    {
        /// <summary>
        /// Quests that are currently in progress (EQuestStatus.Started = 2).
        /// </summary>
        public List<QuestData> Started { get; init; } = [];

        /// <summary>
        /// Quests ready to be accepted from traders (EQuestStatus.AvailableForStart = 1).
        /// </summary>
        public List<QuestData> AvailableForStart { get; init; } = [];

        /// <summary>
        /// Quests ready to be turned in (EQuestStatus.AvailableForFinish = 3).
        /// </summary>
        public List<QuestData> AvailableForFinish { get; init; } = [];
    }

    /// <summary>
    /// Reads active quest state from EFT player Profile memory.
    /// Returns quests with Status=Started (EQuestStatus 2) and their completed conditions.
    ///
    /// Memory path: Profile + 0x98 (QuestsData) -> UnityList&lt;QuestStatusData&gt;
    ///   Each entry: Id (0x10), Status (0x1C), CompletedConditions (0x28)
    ///
    /// Context: This class reads quest STATUS for mission planning (lobby + in-raid fallback).
    /// It is completely independent of QuestManagerV2, which reads quest ZONES
    /// from LocalGameWorld during raids for radar display.
    ///
    /// Quest Status Values (EQuestStatus):
    /// - 0 = Locked
    /// - 1 = AvailableForStart (ready to accept from trader)
    /// - 2 = Started (in progress)
    /// - 3 = AvailableForFinish (ready to turn in)
    /// - 4 = Success
    /// - 5 = Fail
    ///
    /// Verified offsets 2026-02-24 via eft-mission-reader probe.
    /// </summary>
    public static class QuestReader
    {
        /// <summary>
        /// Reads all quests from the player's profile grouped by status.
        /// Returns quests with Status=1 (AvailableForStart), 2 (Started), or 3 (AvailableForFinish).
        /// </summary>
        /// <param name="profile">The profile pointer address.</param>
        /// <returns>
        /// AvailableQuests with quests grouped by status.
        /// Returns empty lists on error or if no matching quests.
        /// </returns>
        public static AvailableQuests ReadAvailableQuests(ulong profile)
        {
            var started = new List<QuestData>();
            var availableForStart = new List<QuestData>();
            var availableForFinish = new List<QuestData>();

            if (profile == 0)
            {
                XMLogging.WriteLine("[QuestReader] Invalid profile address (0)");
                return new AvailableQuests();
            }

            try
            {
                // Read condition counters first (used to populate each QuestData)
                var conditionCounters = ReadConditionCounters(profile);

                // Read QuestsData pointer from profile
                var questsDataPtr = Memory.ReadPtr(profile + Offsets.Profile.QuestsData);

                if (questsDataPtr == 0)
                {
                    XMLogging.WriteLine("[QuestReader] QuestsData pointer is null");
                    return new AvailableQuests();
                }

                // QuestsData points to UnityList<QuestStatusData>
                using var questsList = UnityList<ulong>.Create(questsDataPtr, false);

                foreach (var qDataEntry in questsList)
                {
                    try
                    {
                        // Read quest status
                        var qStatus = Memory.ReadValue<int>(qDataEntry + Offsets.QuestData.Status);

                        // Only process quests we care about:
                        // 1 = AvailableForStart, 2 = Started, 3 = AvailableForFinish
                        if (qStatus != 1 && qStatus != 2 && qStatus != 3)
                            continue;

                        // Read quest ID string
                        var qIdPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.Id);
                        var qId = Memory.ReadUnityString(qIdPtr);

                        if (string.IsNullOrEmpty(qId))
                            continue;

                        // Read completed conditions
                        var completedPtr = Memory.ReadPtr(qDataEntry + Offsets.QuestData.CompletedConditions);
                        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (completedPtr != 0)
                        {
                            ReadCompletedConditions(completedPtr, completed);
                        }

                        var questData = new QuestData
                        {
                            Id = qId,
                            CompletedConditions = completed,
                            ConditionCounters = conditionCounters
                        };

                        // Categorize by status
                        switch (qStatus)
                        {
                            case 1: // AvailableForStart
                                availableForStart.Add(questData);
                                break;
                            case 2: // Started
                                started.Add(questData);
                                break;
                            case 3: // AvailableForFinish
                                availableForFinish.Add(questData);
                                break;
                        }
                    }
                    catch
                    {
                        // Skip invalid quest entries - don't fail the entire read
                    }
                }

                if (started.Count > 0 || availableForStart.Count > 0 || availableForFinish.Count > 0)
                {
                    XMLogging.WriteLine($"[QuestReader] Found {started.Count} Started, {availableForStart.Count} AvailableForStart, {availableForFinish.Count} AvailableForFinish quests");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[QuestReader] Error reading quests: {ex.Message}");
            }

            return new AvailableQuests
            {
                Started = started,
                AvailableForStart = availableForStart,
                AvailableForFinish = availableForFinish
            };
        }

        /// <summary>
        /// Reads all active (Started) quests from the player's profile.
        /// Filters to only return quests with Status=Started (2).
        /// This is a convenience method that calls ReadAvailableQuests internally.
        /// </summary>
        /// <param name="profile">The profile pointer address.</param>
        /// <returns>
        /// List of QuestData for active quests.
        /// Returns empty list on error or if no active quests.
        /// </returns>
        public static List<QuestData> ReadQuests(ulong profile)
        {
            return ReadAvailableQuests(profile).Started;
        }

        /// <summary>
        /// Reads TaskConditionCounters from the player profile.
        /// Memory path: Profile + 0x90 -> Dictionary&lt;MongoID, TaskConditionCounter&gt;
        /// Each TaskConditionCounter value pointer -> object + 0x40 -> _value (int)
        /// </summary>
        /// <param name="profile">The profile pointer address.</param>
        /// <returns>Dictionary mapping condition ID string to current counter value.</returns>
        public static Dictionary<string, int> ReadConditionCounters(ulong profile)
        {
            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (profile == 0)
                return counters;

            try
            {
                var dictPtr = Memory.ReadPtr(profile + Offsets.Profile.TaskConditionCounters);
                if (dictPtr == 0)
                    return counters;

                // Dictionary<MongoID, TaskConditionCounter> — value is a pointer to the counter object
                using var dict = MemDictionary<Types.MongoID, ulong>.Get(dictPtr);

                foreach (var entry in dict)
                {
                    try
                    {
                        if (entry.Key.StringID == 0)
                            continue;

                        var conditionId = Memory.ReadUnityString(entry.Key.StringID);
                        if (string.IsNullOrEmpty(conditionId))
                            continue;

                        var counterPtr = entry.Value;
                        if (counterPtr == 0)
                            continue;

                        // TaskConditionCounter object: fields at +0x10, _value at fields+0x30 = object+0x40
                        var value = Memory.ReadValue<int>(counterPtr + 0x40);

                        counters[conditionId] = value;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[QuestReader] Error reading condition counters: {ex.Message}");
            }

            return counters;
        }

        /// <summary>
        /// Reads the CompletedConditions HashSet directly (no CompletedConditionsCollection wrapper).
        /// The pointer at QuestData.CompletedConditions (0x28) points directly to a HashSet&lt;MongoID&gt;.
        /// </summary>
        /// <param name="hashSetPtr">Pointer to HashSet&lt;MongoID&gt; (read from offset 0x28).</param>
        /// <param name="target">HashSet to populate with condition IDs.</param>
        private static void ReadCompletedConditions(ulong hashSetPtr, HashSet<string> target)
        {
            try
            {
                if (hashSetPtr == 0) return;

                // Use the existing UnityHashSet<MongoID> class directly
                // The pointer at offset 0x28 is already the HashSet, not a CompletedConditionsCollection wrapper
                using var hashSet = UnityHashSet<Types.MongoID>.Create(hashSetPtr, false);
                foreach (var entry in hashSet.Span)
                {
                    var mongoId = entry.Value;
                    // Read the string pointer from MongoID.StringID and get the string
                    var condId = Memory.ReadUnityString(mongoId.StringID);
                    if (!string.IsNullOrEmpty(condId))
                    {
                        target.Add(condId);
                    }
                }
            }
            catch
            {
                // Swallow - partial completed conditions are acceptable
            }
        }
    }
}
