namespace eft_dma_radar.UI.ESP
{
    public enum ESPPlayerRenderMode : int
    {
        /// <summary>
        /// No player bones/box are rendered. Only a dot on 'center of mass'.
        /// </summary>
        None = 0,
        /// <summary>
        /// Full player bones are rendered.
        /// </summary>
        Bones = 1,
        /// <summary>
        /// Box ESP.
        /// </summary>
        Box = 2,
        /// <summary>
        /// Renders a dot showing head.
        /// </summary>
        HeadDot = 3
    }
}
