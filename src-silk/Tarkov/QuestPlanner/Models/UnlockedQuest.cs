namespace eft_dma_radar.Silk.Tarkov.QuestPlanner.Models
{
    /// <summary>
    /// A quest that will be unlocked by completing quests on this map.
    /// </summary>
    internal sealed class UnlockedQuest
    {
        public string QuestName { get; init; } = string.Empty;

        /// <summary>First map from the unlocked quest's objectives, or "Any" if none.</summary>
        public string MapName { get; init; } = string.Empty;
    }
}
