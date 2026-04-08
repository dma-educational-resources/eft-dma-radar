namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Per-objective data within a quest plan, carrying completion state from memory.
/// </summary>
public sealed record ObjectiveInfo(
    string Id,
    string Description,
    bool IsCompleted,
    int CurrentCount = 0,
    int TargetCount = 0,
    string Type = ""
)
{
    public bool HasProgress => TargetCount > 1;
    public string ProgressText => HasProgress ? $"{CurrentCount}/{TargetCount}" : string.Empty;
};

/// <summary>
/// A find-in-raid item pair collapsed into a single progress row for the "Find in raid" category.
/// </summary>
public sealed record FirItemInfo(
    string QuestName,
    string ItemShortName,
    int CurrentCount,
    int TargetCount
)
{
    public string ProgressText => $"{CurrentCount}/{TargetCount}";
}

/// <summary>
/// A quest where the only remaining incomplete objectives are giveQuestItem —
/// the player has the item but hasn't handed it to the trader yet.
/// </summary>
public sealed record HandOverItemInfo(
    string QuestName,
    string ItemShortName
)
{
    public string DisplayText => $"{QuestName} \u2014 Hand over {ItemShortName}";
}

/// <summary>
/// Per-quest data within a map plan: objectives on this map and items to bring.
/// </summary>
public sealed class QuestPlan
{
    public string QuestName { get; init; } = string.Empty;
    public IReadOnlyList<ObjectiveInfo> Objectives { get; init; } = [];

    /// <summary>
    /// Items to bring in — excludes FIR items that must be found/handed during raid.
    /// </summary>
    public IReadOnlyList<BringItem> BringItems { get; init; } = [];
}
