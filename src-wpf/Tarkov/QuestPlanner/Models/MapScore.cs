namespace eft_dma_radar.Tarkov.QuestPlanner.Models;

/// <summary>
/// Mutable accumulator used internally during session plan scoring. Not exposed in the summary.
/// </summary>
public class MapScore
{
    public string MapId { get; }
    public string MapName { get; }
    public int ObjectiveCount { get; set; }
    public int UnlockCount { get; set; }
    public HashSet<string> QuestIds { get; } = [];
    public HashSet<string> FinishableQuestIds { get; } = [];

    public MapScore(string mapId, string mapName)
    {
        MapId = mapId;
        MapName = mapName;
    }
}
