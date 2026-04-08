namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Type of item required for quest completion.
/// </summary>
public enum BringItemType
{
    Key,
    QuestItem
}

/// <summary>
/// Single bring-list entry representing an item to bring to a map.
/// </summary>
public sealed class BringItem
{
    /// <summary>
    /// Item or key alternatives — any one suffices (e.g., ["Key A", "Key B"]).
    /// </summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public string QuestName { get; init; } = string.Empty;
    public BringItemType Type { get; init; }
    public int Count { get; init; } = 1;
}
