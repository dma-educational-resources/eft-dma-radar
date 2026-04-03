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
                byte[] fontFamilyRegular, fontFamilyBold, fontFamilyItalic, fontFamilyMedium;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eft_dma_radar.NeoSansStdRegular.otf"))
                {
                    fontFamilyRegular = new byte[stream!.Length];
                    stream.ReadExactly(fontFamilyRegular);
                }
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eft_dma_radar.NeoSansStdBold.otf"))
                {
                    fontFamilyBold = new byte[stream!.Length];
                    stream.ReadExactly(fontFamilyBold);
                }
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eft_dma_radar.NeoSansStdItalic.otf"))
                {
                    fontFamilyItalic = new byte[stream!.Length];
                    stream.ReadExactly(fontFamilyItalic);
                }
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("eft_dma_radar.NeoSansStdMedium.otf"))
                {
                    fontFamilyMedium = new byte[stream!.Length];
                    stream.ReadExactly(fontFamilyMedium);
                }
                // SKTypeface.FromStream reads the stream synchronously; wrap each
                // MemoryStream in a using block so the IDisposable is honoured
                // even though MemoryStream only holds managed memory.
                using (var ms = new MemoryStream(fontFamilyRegular, false))
                    SKFontFamilyRegular = SKTypeface.FromStream(ms);
                using (var ms = new MemoryStream(fontFamilyBold, false))
                    SKFontFamilyBold = SKTypeface.FromStream(ms);
                using (var ms = new MemoryStream(fontFamilyItalic, false))
                    SKFontFamilyItalic = SKTypeface.FromStream(ms);
                using (var ms = new MemoryStream(fontFamilyMedium, false))
                    SKFontFamilyMedium = SKTypeface.FromStream(ms);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("ERROR Loading Custom Fonts!", ex);
            }
        }
    }
}
