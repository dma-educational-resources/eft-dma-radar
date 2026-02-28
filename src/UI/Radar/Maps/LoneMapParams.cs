using SkiaSharp;

namespace eft_dma_radar.Common.Maps
{
    /// <summary>
    /// Contains multiple map parameters used by the GUI.
    /// </summary>
    public sealed class XMMapParams
    {
        /// <summary>
        /// Currently loaded Map File.
        /// </summary>
        public XMMapConfig Map { get; init; }
        /// <summary>
        /// Rectangular 'zoomed' bounds of the Bitmap to display.
        /// </summary>
        public SKRect Bounds { get; init; }
        /// <summary>
        /// Regular -> Zoomed 'X' Scale correction.
        /// </summary>
        public float XScale { get; init; }
        /// <summary>
        /// Regular -> Zoomed 'Y' Scale correction.
        /// </summary>
        public float YScale { get; init; }
    }
}
