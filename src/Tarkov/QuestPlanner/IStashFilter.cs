namespace eft_dma_radar.Tarkov.QuestPlanner;

/// <summary>
/// Interface for stash ownership queries.
/// Used by MissionService to filter bring-list items based on player inventory.
/// </summary>
public interface IStashFilter
{
    /// <summary>
    /// Checks if the player owns at least one of the item with the given template ID.
    /// Template IDs match StashItem.TemplateId from Phase 1 and MarkerItemClass.Id /
    /// ObjectiveQuestItem.Id from tarkov.dev data.
    /// </summary>
    /// <param name="templateId">The item template ID to check ownership for.</param>
    /// <returns>True if the player owns at least one of the item; false otherwise.</returns>
    bool Owns(string templateId);

    /// <summary>
    /// Indicates whether the stash reading is available.
    /// When false, the bring list shows ALL required items and a "Stash not connected" label.
    /// </summary>
    bool IsConnected { get; }
}
