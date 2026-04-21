namespace eft_dma_radar.Silk.Tarkov.QuestPlanner.Models
{
    /// <summary>
    /// Single quest entry read from <see cref="Offsets.Profile.QuestsData"/>.
    /// Owned by the planner layer so it can run independently from the in-raid QuestManager.
    /// </summary>
    internal sealed class QuestData
    {
        public required string Id { get; init; }

        /// <summary>Condition IDs (MongoID.StringID) already completed for this quest.</summary>
        public required HashSet<string> CompletedConditions { get; init; }

        /// <summary>Shared per-profile condition counter map (conditionId -> value).</summary>
        public IReadOnlyDictionary<string, int> ConditionCounters { get; init; }
            = new Dictionary<string, int>(0, StringComparer.Ordinal);

        public override string ToString() => $"Quest[{Id}] Completed:{CompletedConditions.Count}";
    }

    /// <summary>
    /// Quests grouped by <c>EQuestStatus</c>. Only Started (2), AvailableForStart (1) and
    /// AvailableForFinish (3) are populated by the reader.
    /// </summary>
    internal sealed class AvailableQuests
    {
        public List<QuestData> Started { get; init; } = [];
        public List<QuestData> AvailableForStart { get; init; } = [];
        public List<QuestData> AvailableForFinish { get; init; } = [];
    }
}
