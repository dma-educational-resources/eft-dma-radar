using SkiaSharp;

namespace eft_dma_radar.UI.Misc
{
    public static class SkiaExtensions
    {
        /// <summary>
        /// Returns true if the point contains valid finite screen coordinates.
        /// Prevents long ESP lines caused by NaN / Infinity / garbage values.
        /// </summary>
        public static bool IsFinite(this SKPoint p)
        {
            return
                !float.IsNaN(p.X) &&
                !float.IsNaN(p.Y) &&
                !float.IsInfinity(p.X) &&
                !float.IsInfinity(p.Y);
        }
    }
}