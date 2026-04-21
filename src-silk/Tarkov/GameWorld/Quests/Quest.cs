namespace eft_dma_radar.Silk.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Types of quest objectives.
    /// </summary>
    internal enum QuestObjectiveType
    {
        FindItem,
        PlaceItem,
        VisitLocation,
        Other,
    }

    /// <summary>
    /// Represents a quest with its objectives and completion status.
    /// </summary>
    internal sealed class Quest
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool KappaRequired { get; init; }
        public List<QuestObjective> Objectives { get; init; } = [];
        public HashSet<string> RequiredItems { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CompletedConditions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True if all objectives are completed.</summary>
        public bool IsCompleted
        {
            get
            {
                for (int i = 0; i < Objectives.Count; i++)
                {
                    if (!Objectives[i].IsCompleted)
                        return false;
                }
                return Objectives.Count > 0;
            }
        }
    }

    /// <summary>
    /// Represents a quest objective with completion status and requirements.
    /// </summary>
    internal sealed class QuestObjective
    {
        public string Id { get; init; } = string.Empty;
        public QuestObjectiveType Type { get; init; }
        public bool Optional { get; init; }
        public string Description { get; init; } = string.Empty;
        public bool IsCompleted { get; init; }
        public List<string> RequiredItemIds { get; init; } = [];
        public List<QuestLocation> LocationObjectives { get; init; } = [];
    }
}
