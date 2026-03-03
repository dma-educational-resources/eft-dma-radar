namespace eft_dma_radar.Tarkov.MissionPlanner;

/// <summary>
/// Default stash filter used when CORE-04 (stash reading) is not yet implemented.
/// Returns false for all ownership checks, causing the bring list to show all required items.
/// </summary>
public sealed class NullStashFilter : IStashFilter
{
    /// <summary>
    /// Singleton instance for reuse throughout the application.
    /// </summary>
    public static readonly NullStashFilter Instance = new();

    /// <inheritdoc/>
    public bool Owns(string templateId) => false;

    /// <inheritdoc/>
    public bool IsConnected => false;

    private NullStashFilter() { }
}
