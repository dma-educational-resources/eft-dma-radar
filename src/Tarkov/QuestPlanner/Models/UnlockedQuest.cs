namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Information about a quest that will be unlocked by completing quests on this map.
/// </summary>
public sealed class UnlockedQuest
{
    /// <summary>
    /// Name of the quest that will be unlocked.
    /// </summary>
    public string QuestName { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the map where the unlocked quest's objectives are located.
    /// Uses the first map from the quest's objectives, or "Any" if no specific map.
    /// </summary>
    public string MapName { get; init; } = string.Empty;
}
