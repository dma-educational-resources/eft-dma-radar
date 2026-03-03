namespace eft_dma_radar.Tarkov.MissionPlanner.Models;

/// <summary>
/// Type of item required for quest completion.
/// </summary>
public enum BringItemType
{
    /// <summary>
    /// A key required to access an area or container.
    /// </summary>
    Key,

    /// <summary>
    /// A quest item required to complete an objective.
    /// </summary>
    QuestItem
}

/// <summary>
/// Single bring-list entry representing an item to bring to a map.
/// </summary>
public sealed class BringItem
{
    /// <summary>
    /// Single item or key alternatives (e.g., ["Factory key"] or ["Key A", "Key B"]).
    /// Multiple entries indicate any of the alternatives will work.
    /// </summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>
    /// The quest requiring this item.
    /// </summary>
    public string QuestName { get; init; } = string.Empty;

    /// <summary>
    /// The type of item (Key or QuestItem).
    /// </summary>
    public BringItemType Type { get; init; }

    /// <summary>
    /// Number of this item required (e.g., 3 for a quest needing 3 MS2000 Markers).
    /// </summary>
    public int Count { get; init; } = 1;
}
