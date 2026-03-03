namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Internal accumulation type for map scoring during session plan computation.
/// This is a mutable class used internally during scoring, not exposed in the summary.
/// </summary>
public class MapScore
{
    /// <summary>
    /// Canonical map key from TaskMapElement.NameId.
    /// </summary>
    public string MapId { get; }

    /// <summary>
    /// Display name from TaskMapElement.Name.
    /// </summary>
    public string MapName { get; }

    /// <summary>
    /// Count of objectives that can be completed on this map.
    /// Incremented during scoring.
    /// </summary>
    public int ObjectiveCount { get; set; }

    /// <summary>
    /// Count of distinct quests unlocked by completing quests on this map.
    /// Computed before ranking to prioritize maps that open up more quest progression.
    /// </summary>
    public int UnlockCount { get; set; }

    /// <summary>
    /// Set of distinct quest IDs contributing objectives to this map.
    /// </summary>
    public HashSet<string> QuestIds { get; } = [];

    /// <summary>
    /// Set of quest IDs that can be fully completed on this map alone
    /// (all remaining objectives are on this single map).
    /// Multi-map quests are excluded from this set.
    /// </summary>
    public HashSet<string> FinishableQuestIds { get; } = [];

    /// <summary>
    /// Creates a new MapScore for the specified map.
    /// </summary>
    public MapScore(string mapId, string mapName)
    {
        MapId = mapId;
        MapName = mapName;
    }
}
