using eft_dma_radar.Common.Unity;
using SkiaSharp;

namespace eft_dma_radar.Common.Misc
{
    public static class SharedPaints
    {
        public static SKPaint PaintBitmap { get; } = new()
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        public static SKPaint PaintBitmapAlpha { get; } = new()
        {
            Color = SKColor.Empty.WithAlpha(127),
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        /// <summary>
        /// Gets an SKColorFilter that will reduce an image's brightness level.
        /// </summary>
        /// <param name="brightnessFactor">Adjust this value between 0 (black) and 1 (original brightness), where values less than 1 reduce brightness</param>
        /// <returns>SKColorFilter Object.</returns>
        public static SKColorFilter GetDarkModeColorFilter(float brightnessFactor)
        {
            float[] colorMatrix = {
                brightnessFactor, 0, 0, 0, 0, // Red channel
                0, brightnessFactor, 0, 0, 0, // Green channel
                0, 0, brightnessFactor, 0, 0, // Blue channel
                0, 0, 0, 1, 0, // Alpha channel
            };
            return SKColorFilter.CreateColorMatrix(colorMatrix);
        }
    }
    public static class SKColorExtensions
    {
        public static SKColor ToSKColor(this UnityColor color)
        {
            return new SKColor((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), (byte)(color.A * 255));
        }
    }    
}
