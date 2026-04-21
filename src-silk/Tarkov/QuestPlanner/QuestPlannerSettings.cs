namespace eft_dma_radar.Silk.Tarkov.QuestPlanner
{
    /// <summary>
    /// Runtime settings that influence how the quest planner scores/filters quests.
    /// Persisted in SilkConfig.
    /// </summary>
    internal sealed class QuestPlannerSettings
    {
        /// <summary>Restrict completable objectives to Kappa-required quests only.</summary>
        public bool KappaFilter { get; set; }
    }
}
