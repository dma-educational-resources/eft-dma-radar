namespace eft_dma_radar.Tarkov.QuestPlanner
{
    /// <summary>
    /// Quest state data contract between QuestMemoryReader and QuestPlanBuilder.
    /// Represents a single active (Status=Started) quest from the player's profile memory.
    /// </summary>
    /// <remarks>
    /// Memory source: Profile.QuestsData (offset 0x98) -> UnityList&lt;QuestStatusData&gt;
    /// Only quests with EQuestStatus.Started (2) are included.
    /// This is the ONLY type passed from the memory layer to QuestPlanBuilder --
    /// no direct coupling to QuestManagerV2 types.
    /// </remarks>
    public sealed class QuestData
    {
        /// <summary>
        /// Quest ID as a MongoDB ObjectId string (24-char hex).
        /// Maps to EftDataManager.TaskData keys for quest metadata lookup.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Set of completed condition IDs for this quest.
        /// Each entry is a MongoDB ObjectId string identifying a completed objective.
        /// Empty if no conditions are completed yet.
        /// </summary>
        public required HashSet<string> CompletedConditions { get; init; }

        /// <summary>
        /// Per-condition progress counters from TaskConditionCounters (Profile+0x90).
        /// Maps condition ID -> current counter value. Shared across all quests from the same profile read.
        /// </summary>
        public IReadOnlyDictionary<string, int> ConditionCounters { get; init; } = new Dictionary<string, int>();

        /// <inheritdoc/>
        public override string ToString() =>
            $"Quest[{Id}] Completed: {CompletedConditions.Count}";
    }
}
