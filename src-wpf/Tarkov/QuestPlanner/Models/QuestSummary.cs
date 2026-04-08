namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Root DTO returned by QuestPlanBuilder. Contains the full ordered session plan.
/// </summary>
public sealed class QuestSummary
{
    /// <summary>
    /// Maps ordered by priority, highest first.
    /// </summary>
    public IReadOnlyList<MapPlan> Maps { get; init; } = [];

    /// <summary>
    /// Active quests with no map attribution (e.g. Gunsmith series). Shown as "All Maps" section.
    /// </summary>
    public IReadOnlyList<QuestPlan> AllMapsQuests { get; init; } = [];
    public int TotalActiveQuests { get; init; }
    public int TotalCompletableObjectives { get; init; }

    /// <summary>
    /// Traders with quests ready to start (AvailableForStart status).
    /// </summary>
    public IReadOnlyList<string> AvailableForStartTraders { get; init; } = [];

    /// <summary>
    /// Traders with quests ready to turn in (AvailableForFinish status).
    /// </summary>
    public IReadOnlyList<string> AvailableForFinishTraders { get; init; } = [];

    /// <summary>
    /// FIR item pairs collapsed into progress rows for the "Find in raid" category.
    /// </summary>
    public IReadOnlyList<FirItemInfo> FirItems { get; init; } = [];

    /// <summary>
    /// Quests where all remaining objectives are giveQuestItem — item collected, not yet handed in.
    /// </summary>
    public IReadOnlyList<HandOverItemInfo> HandOverItems { get; init; } = [];
    public DateTime ComputedAt { get; init; } = DateTime.UtcNow;
}
