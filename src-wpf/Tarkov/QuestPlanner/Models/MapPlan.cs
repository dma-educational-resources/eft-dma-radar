namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Per-map entry in the session plan, ordered by priority.
/// </summary>
public sealed class MapPlan
{
    public string MapId { get; init; } = string.Empty;
    public string MapName { get; init; } = string.Empty;

    /// <summary>
    /// True for the top-ranked map (highest priority recommendation).
    /// </summary>
    public bool IsRecommended { get; init; }
    public int CompletableObjectiveCount { get; init; }
    public int ActiveQuestCount { get; init; }
    public IReadOnlyList<QuestPlan> Quests { get; init; } = [];
    public IReadOnlyList<UnlockedQuest> UnlockedQuests { get; init; } = [];

    /// <summary>
    /// Deduplicated bring list across all quests. Excludes FIR/hand-over items.
    /// </summary>
    public IReadOnlyList<BringItem> FilteredBringList { get; init; } = [];
}
