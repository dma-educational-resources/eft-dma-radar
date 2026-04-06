namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Shared UI state accessible from game logic without depending on any specific UI framework (WPF/Silk.NET).
    /// Both MainWindow and RadarWindow write to this; shared game logic reads from it.
    /// </summary>
    public static class UISharedState
    {
        /// <summary>
        /// The currently moused-over player group ID, if any.
        /// </summary>
        public static int? MouseoverGroup { get; set; }

        /// <summary>
        /// Current UI Scale Value for the active application window.
        /// Delegates to the shared Config instance.
        /// </summary>
        public static float UIScale => ConfigManager.CurrentConfig?.UIScale ?? 1.0f;
    }
}
