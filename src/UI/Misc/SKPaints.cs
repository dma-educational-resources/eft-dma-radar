using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.UI.Misc
{
    internal static class SKPaints
    {
        private static readonly Stopwatch _pulseTimer = Stopwatch.StartNew();
        private static SKColor _currentAsteriskColor = SKColors.Red;

        /// <summary>
        /// Updates the pulsing color for important player indicators. Should be called before using PaintPulsingAsterisk.
        /// </summary>
        public static void UpdatePulsingAsteriskColor()
        {
            var time = _pulseTimer.ElapsedMilliseconds / 1000.0;
            var pulseFactor = (Math.Sin(time * 4) + 1) / 2;
            var greenValue = (byte)(0 + (100 * pulseFactor));
            _currentAsteriskColor = new SKColor(255, greenValue, 0, 255);

            TextPulsingAsterisk.Color = _currentAsteriskColor;
            TextPulsingAsteriskESP.Color = _currentAsteriskColor;
        }

        #region Radar Paints

        public static SKPaint PaintConnectorGroup { get; } = new()
        {
            Color = SKColors.LawnGreen.WithAlpha(60),
            StrokeWidth = 2.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMouseoverGroup { get; } = new()
        {
            Color = SKColors.LawnGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };
                
        public static SKPaint TextMouseoverGroup { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.LawnGreen,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextLocalPlayer { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Green,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextTeammate { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.LimeGreen,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextUSEC { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBEAR { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Blue,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextSpecial { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.HotPink,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextStreamer { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.MediumPurple,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextAimbotLocked { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Blue,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextScav { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextRaider { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("ffc70f"),
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBoss { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Fuchsia,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextFocused { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Coral,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPScav { get; } = new() // Player Scav Text , Tooltip Text
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextMouseover { get; } = new() // Tooltip Text
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDeathMarker { get; } = new()
        {
            Color = SKColors.Black,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        #endregion

        #region Loot Paints
        public static SKPaint PaintLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintImportantLoot { get; } = new()
        {
            Color = SKColors.Turquoise,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniImportantLoot { get; } = new()
        {
            Color = SKColors.Turquoise,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintContainerLoot { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniContainerLoot { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextContainer { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("FFFFCC"),
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.WhiteSmoke,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextImportantLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Turquoise,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextAirdrop { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.YellowGreen,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAirdrop { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniAirdrop { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintCorpse { get; } = new()
        {
            Color = SKColors.Silver,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniCorpse { get; } = new()
        {
            Color = SKColors.Silver,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextCorpse { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Silver,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextMeds { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.LightSalmon,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFood { get; } = new()
        {
            Color = SKColors.CornflowerBlue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniFood { get; } = new()
        {
            Color = SKColors.CornflowerBlue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextFood { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.CornflowerBlue,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBackpacks { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("00b02c"),
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintWeapons { get; } = new()
        {
            Color = SKColor.Parse("ffa756"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniWeapons{ get; } = new()
        {
            Color = SKColor.Parse("ffa756"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextWeapons { get; } = new()
        {
            SubpixelText = true,
            Color = SKColor.Parse("ffa756"),
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint QuestHelperPaint { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint MiniQuestHelperPaint { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint QuestHelperText { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.DeepPink,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint QuestHelperOutline { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 2.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintQuestItem { get; } = new()
        {
            Color = SKColors.YellowGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniQuestItem { get; } = new()
        {
            Color = SKColors.YellowGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextQuestItem { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.YellowGreen,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintWishlistItem { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniWishlistItem { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextWishlistItem { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static readonly SKPaint TextDoorOpen = new SKPaint
        {
            SubpixelText = true,
            Color = SKColors.Green,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorOpen { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static readonly SKPaint TextDoorShut = new SKPaint
        {
            SubpixelText = true,
            Color = SKColors.Orange,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorShut { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static readonly SKPaint TextDoorLocked = new SKPaint
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorLocked { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static readonly SKPaint TextDoorInteracting = new SKPaint
        {
            SubpixelText = true,
            Color = SKColors.Blue,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorInteracting{ get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static readonly SKPaint TextDoorBreaching = new SKPaint
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorBreaching { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextSwitch { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Orange,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilTransit { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Orange,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilInactive { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Gray,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilOpen { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.LimeGreen,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilPending { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Yellow,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilClosed{ get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        #endregion

        #region Render/Misc Paints

        public static SKPaint PaintTransparentBacker { get; } = new()
        {
            Color = SKColors.Black.WithAlpha(0xBE), // Transparent backer
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill
        };

        public static SKPaint TextRadarStatus { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 48,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            TextAlign = SKTextAlign.Left
        };

        public static SKPaint TextStatusSmall { get; } = new SKPaint()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 13,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextStatusSmallEsp { get; } = new SKPaint()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 13,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExplosives { get; } = new()
        {
            Color = SKColors.OrangeRed,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExplosivesDanger { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExplosives { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.OrangeRed,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExplosivesDanger { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 12,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyRegular,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilOpen { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilTransit { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilPending { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilClosed { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilInactive { get; } = new()
        {
            Color = SKColors.Gray,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintSwitch { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextOutline { get; } = new()
        {
            SubpixelText = true,
            IsAntialias = true,
            Color = SKColors.Black,
            TextSize = 12f,
            IsStroke = true,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            Typeface = CustomFonts.SKFontFamilyRegular
        };

        /// <summary>
        /// Only utilize this paint on the Radar UI Thread. StrokeWidth is modified prior to each draw call.
        /// *NOT* Thread safe to use!
        /// </summary>
        public static SKPaint ShapeOutline { get; } = new()
        {
            Color = SKColors.Black,
            /*StrokeWidth = ??,*/ // Compute before use
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPulsingAsterisk { get; } = new()
        {
            Color = SKColors.Red, // Initial color, will be updated
            IsAntialias = true,
            TextSize = 24f,
            FakeBoldText = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPulsingAsteriskOutline { get; } = new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            TextSize = 24f,
            IsStroke = true,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            FakeBoldText = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPulsingAsteriskESP { get; } = new()
        {
            Color = SKColors.Red, // Initial color, will be updated
            SubpixelText = true,
            IsStroke = false,
            TextSize = 18f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPulsingAsteriskOutlineESP { get; } = new()
        {
            Color = SKColors.Black,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 18f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        #endregion

        #region ESP Paints
        public static SKPaint PaintVisible { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextVisible { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };
        public static SKPaint PaintUSECESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextUSECESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBEARESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBEARESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBEARESPAligned { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintScavESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextScavESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintRaiderESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextRaiderESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBossESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBossESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAimbotLockedESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };
        public static SKPaint PhysicsTextPaint { get; } = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 12,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextAlign = SKTextAlign.Center
        };
        public static SKPaint PhysicsFillPaint { get; } = new SKPaint
        {
            Color = SKColors.Red.WithAlpha(50),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        public static SKPaint GetPaintForDistance(float distance)
        {
            return new SKPaint
            {
                Color = SKColors.Yellow.WithAlpha((byte)(255 - (distance / 100f) * 200)),
                StrokeWidth = 1f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
        }
        public static SKPaint TextAimbotLockedESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFocusedESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextFocusedESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintStreamerESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextStreamerESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintSpecialESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextSpecialESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintPlayerScavESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextPlayerScavESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFriendlyESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextFriendlyESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextLootESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintCorpseESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextCorpseESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintImpLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextImpLootESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAirdropESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextAirdropESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintContainerLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextContainerLootESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 11f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMedsESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextMedsESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFoodESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextFoodESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBackpackESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBackpackESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintWeaponsESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextWeaponsESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };


        public static SKPaint PaintQuestItemESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextQuestItemESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintWishlistItemESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextWishlistItemESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintQuestHelperESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextQuestHelperESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExplosiveESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExplosiveRadiusESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExplosiveESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilOpenESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilOpenESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilPendingESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilPendingESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilClosedESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilClosedESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilInactiveESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilInactiveESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextExfilTransitESP { get; } = new()
        {
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintExfilTransitESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintSwitchESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextSwitchesESP { get; } = new()
        {
            Color = SKColors.Orange,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorOpenESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextDoorOpenESP { get; } = new()
        {
            Color = SKColors.Green,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorShutESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextDoorShutESP { get; } = new()
        {
            Color = SKColors.Orange,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorLockedESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextDoorLockedESP { get; } = new()
        {
            Color = SKColors.Red,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorInteractingESP { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextDoorInteractingESP { get; } = new()
        {
            Color = SKColors.Blue,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintDoorBreachingESP { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextDoorBreachingESP { get; } = new()
        {
            Color = SKColors.Yellow,
            SubpixelText = true,
            IsStroke = false,
            TextSize = 12f,
            FakeBoldText = true,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintESPHealthBar = new()
        {
            Color = SKColors.Green,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintESPHealthBarBg = new()
        {
            Color = new SKColor(30, 30, 30, 200),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintESPHealthBarBorder = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        #region ESP Readonly Paints

        public static SKPaint PaintCrosshairESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1.75f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintCrosshairESPDot { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintHighAlertAimlineESP { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintHighAlertBorderESP { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintBasicESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBasicESP { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextBasicESPLeftAligned { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextESPFPS { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextESPRaidStats { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextESPStatusText { get; } = new SKPaint()
        {
            SubpixelText = true,
            Color = SKColors.Red,
            IsStroke = false,
            TextSize = 13,
            TextAlign = SKTextAlign.Center,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextMagazineESP { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 42f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyBold,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextMagazineInfoESP { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 16f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyItalic,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextEnergyHydrationBarESP { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 12f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextEnergyHydrationBarOutlineESP { get; } = new()
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            TextSize = 12f,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintEnergyFillESP { get; } = new()
        {
            Color = SKColor.Parse("#D4C48A"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintHydrationFillESP { get; } = new()
        {
            Color = SKColor.Parse("#5B9BD5"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintEnergyHydrationBackgroundESP { get; } = new()
        {
            Color = SKColors.Black.WithAlpha(180),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintFireportAimESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAimbotFOVESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextESPClosestPlayer { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 13f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint TextESPTopLoot { get; } = new()
        {
            SubpixelText = true,
            Color = SKColors.White,
            IsStroke = false,
            TextSize = 13f,
            TextAlign = SKTextAlign.Left,
            TextEncoding = SKTextEncoding.Utf8,
            IsAntialias = true,
            Typeface = CustomFonts.SKFontFamilyMedium,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintAimbotLockedLineESP { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniRadarOutlineESP { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        public static SKPaint PaintMiniRadarResizeHandleESP { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 2,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            FilterQuality = SKFilterQuality.Low
        };

        #endregion

        #endregion
    }
}