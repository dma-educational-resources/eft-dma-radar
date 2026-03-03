namespace eft_dma_radar.Tarkov.MissionPlanner.Models;

/// <summary>
/// Per-objective data within a mission plan, carrying completion state from memory.
/// </summary>
public sealed record ObjectiveInfo(
    /// <summary>Objective condition ID, matched against QuestData.CompletedConditions.</summary>
    string Id,
    /// <summary>Human-readable objective text.</summary>
    string Description,
    /// <summary>True if this objective ID appears in the active quest's CompletedConditions.</summary>
    bool IsCompleted,
    /// <summary>Current progress count from TaskConditionCounters (0 if no counter found).</summary>
    int CurrentCount = 0,
    /// <summary>Target count from tarkov.dev ObjectiveElement.Count. Progress is displayable when > 1.</summary>
    int TargetCount = 0,
    /// <summary>Objective type string from tarkov.dev (e.g., "findQuestItem", "giveQuestItem", "mark", "plantItem").</summary>
    string Type = ""
)
{
    /// <summary>
    /// Whether this objective has displayable partial progress (e.g., "2/3").
    /// </summary>
    public bool HasProgress => TargetCount > 1;

    /// <summary>
    /// Formatted progress string for UI display (e.g., "2/3").
    /// </summary>
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
    /// <summary>Formatted progress text, e.g., "1/3".</summary>
    public string ProgressText => $"{CurrentCount}/{TargetCount}";
}

/// <summary>
/// A quest where the only remaining incomplete objectives are giveQuestItem — player has the item but
/// hasn't handed it to the trader yet.
/// </summary>
public sealed record HandOverItemInfo(
    string QuestName,
    string ItemShortName
)
{
    /// <summary>Display line for the "Hand over items" banner, e.g., "Saving the Mole — Hand over HDD".</summary>
    public string DisplayText => $"{QuestName} \u2014 Hand over {ItemShortName}";
}

/// <summary>
/// Per-mission data within a map plan.
/// Contains the quest name, its objectives on this map, and specific items to bring.
/// </summary>
public sealed class MissionPlan
{
    /// <summary>
    /// The quest/mission name.
    /// </summary>
    public string MissionName { get; init; } = string.Empty;

    /// <summary>
    /// List of objectives for this mission on the current map with completion state.
    /// </summary>
    public IReadOnlyList<ObjectiveInfo> Objectives { get; init; } = [];

    /// <summary>
    /// Items specific to this mission that should be brought into the raid.
    /// Filtered to exclude items that must be found/handed during raid (FIR items).
    /// </summary>
    public IReadOnlyList<BringItem> BringItems { get; init; } = [];
}
