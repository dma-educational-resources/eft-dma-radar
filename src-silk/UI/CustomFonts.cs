namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Loads embedded Neo Sans Std font resources for SkiaSharp rendering.
    /// </summary>
    internal static class CustomFonts
    {
        public static SKTypeface Regular { get; }
        public static SKTypeface Medium { get; }

        static CustomFonts()
        {
            Regular = LoadFont("eft_dma_radar.Silk.NeoSansStdRegular.otf");
            Medium = LoadFont("eft_dma_radar.Silk.NeoSansStdMedium.otf");
        }

        private static SKTypeface LoadFont(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found.");
            return SKTypeface.FromStream(stream);
        }
    }
}
