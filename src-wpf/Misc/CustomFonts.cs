using System.IO;

namespace eft_dma_radar.Misc
{
    public static class CustomFonts
    {
        /// <summary>
        /// Neo Sans Std Regular
        /// </summary>
        public static SKTypeface SKFontFamilyRegular { get; }
        /// <summary>
        /// Neo Sans Std Bold
        /// </summary>
        public static SKTypeface SKFontFamilyBold { get; }
        /// <summary>
        /// Neo Sans Std Italic
        /// </summary>
        public static SKTypeface SKFontFamilyItalic { get; }
        /// <summary>
        /// Neo Sans Std Medium
        /// </summary>
        public static SKTypeface SKFontFamilyMedium { get; }

        static CustomFonts()
        {
            try
            {
                byte[] fontFamilyRegular = ReadResource("eft_dma_radar.NeoSansStdRegular.otf")
                    ?? throw new InvalidOperationException("Required embedded font 'NeoSansStdRegular.otf' not found.");
                byte[]? fontFamilyBold = ReadResource("eft_dma_radar.NeoSansStdBold.otf");
                byte[]? fontFamilyItalic = ReadResource("eft_dma_radar.NeoSansStdItalic.otf");
                byte[]? fontFamilyMedium = ReadResource("eft_dma_radar.NeoSansStdMedium.otf");

                // SKTypeface.FromStream reads the stream synchronously; wrap each
                // MemoryStream in a using block so the IDisposable is honoured
                // even though MemoryStream only holds managed memory. Optional
                // weights fall back to Regular when their resource is not bundled.
                using (var ms = new MemoryStream(fontFamilyRegular, false))
                    SKFontFamilyRegular = SKTypeface.FromStream(ms);
                SKFontFamilyBold = LoadOrFallback(fontFamilyBold, SKFontFamilyRegular);
                SKFontFamilyItalic = LoadOrFallback(fontFamilyItalic, SKFontFamilyRegular);
                SKFontFamilyMedium = LoadOrFallback(fontFamilyMedium, SKFontFamilyRegular);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("ERROR Loading Custom Fonts!", ex);
            }
        }

        private static byte[]? ReadResource(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream is null)
                return null;
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer);
            return buffer;
        }

        private static SKTypeface LoadOrFallback(byte[]? data, SKTypeface fallback)
        {
            if (data is null)
                return fallback;
            using var ms = new MemoryStream(data, false);
            return SKTypeface.FromStream(ms) ?? fallback;
        }
    }
}
