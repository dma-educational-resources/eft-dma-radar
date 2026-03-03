namespace eft_dma_radar.Tarkov.MissionPlanner.Models;

/// <summary>
/// Per-map entry in the session plan.
/// Contains map identification, objective/quest counts, missions, items to bring, and unlock info.
/// </summary>
public sealed class MapPlan
{
    /// <summary>
    /// Canonical map key from TaskMapElement.NameId (e.g., "factory4_day").
    /// </summary>
    public string MapId { get; init; } = string.Empty;

    /// <summary>
    /// Display name from TaskMapElement.Name (e.g., "Factory").
    /// </summary>
    public string MapName { get; init; } = string.Empty;

    /// <summary>
    /// True for the top-ranked map in the session plan (highest priority recommendation).
    /// </summary>
    public bool IsRecommended { get; init; }

    /// <summary>
    /// How many objectives can be completed on this map.
    /// </summary>
    public int CompletableObjectiveCount { get; init; }

    /// <summary>
    /// How many distinct quests have objectives on this map.
    /// </summary>
    public int ActiveQuestCount { get; init; }

    /// <summary>
    /// Missions (quests) that have objectives on this map, with their specific objectives and bring items.
    /// </summary>
    public IReadOnlyList<MissionPlan> Missions { get; init; } = [];

    /// <summary>
    /// Quests that will be unlocked by completing quests on this map.
    /// </summary>
    public IReadOnlyList<UnlockedQuest> UnlockedQuests { get; init; } = [];

    /// <summary>
    /// Aggregated bring list at map level (deduplicated items from all missions).
    /// Excludes items that must be FOUND/HANDED during raid (only items to bring IN).
    /// </summary>
    public IReadOnlyList<BringItem> FilteredBringList { get; init; } = [];

    /// <summary>
    /// Items to bring to this map for quest completion (legacy, unfiltered).
    /// </summary>
    public IReadOnlyList<BringItem> BringList { get; init; } = [];
}
