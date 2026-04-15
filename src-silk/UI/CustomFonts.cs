namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Loads embedded Neo Sans Std font resources for SkiaSharp rendering.
    /// </summary>
    internal static class CustomFonts
    {
        private const string FontResourceName = "eft_dma_radar.Silk.NeoSansStdRegular.otf";

        public static SKTypeface Regular { get; }

        static CustomFonts()
        {
            Regular = LoadFont(FontResourceName);
        }

        /// <summary>
        /// Returns the raw embedded font file bytes.
        /// Used by ImGui contexts that need to load the font from memory.
        /// </summary>
        internal static byte[]? GetEmbeddedFontData()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FontResourceName);
                if (stream is null)
                    return null;

                var data = new byte[stream.Length];
                stream.ReadExactly(data);
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static SKTypeface LoadFont(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' not found.");
            return SKTypeface.FromStream(stream);
        }
    }
}
