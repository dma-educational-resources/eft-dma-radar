namespace eft_dma_radar.Arena.UI
{
    /// <summary>
    /// Loads the embedded Neo Sans Std font for SkiaSharp and ImGui.
    /// </summary>
    internal static class CustomFonts
    {
        private const string FontResourceName = "eft_dma_radar.Arena.NeoSansStdRegular.otf";

        public static SKTypeface Regular { get; } = LoadFont();

        /// <summary>Returns the raw embedded font bytes (null if missing).</summary>
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

        private static SKTypeface LoadFont()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FontResourceName)
                ?? throw new InvalidOperationException($"Embedded font resource '{FontResourceName}' not found.");
            return SKTypeface.FromStream(stream);
        }
    }
}
