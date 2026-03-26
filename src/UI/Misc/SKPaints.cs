using eft_dma_radar.Common.Misc;

namespace eft_dma_radar.UI.Misc
{
    internal static class SKPaints
    {
        private static readonly Stopwatch _pulseTimer = Stopwatch.StartNew();
        private static SKColor _currentAsteriskColor = SKColors.Red;

        #region Shared SKFont Instances

        /// <summary>Regular typeface, 12pt, subpixel. Used by most radar text paints.</summary>
        public static SKFont FontRegular12 { get; } = new(CustomFonts.SKFontFamilyRegular, 12) { Subpixel = true };

        /// <summary>Medium typeface, 12pt, subpixel. Used by most ESP text paints.</summary>
        public static SKFont FontMedium12 { get; } = new(CustomFonts.SKFontFamilyMedium, 12) { Subpixel = true };

        /// <summary>Medium typeface, 11pt, subpixel. Used by TextContainerLootESP.</summary>
        public static SKFont FontMedium11 { get; } = new(CustomFonts.SKFontFamilyMedium, 11) { Subpixel = true };

        /// <summary>Medium typeface, 13pt, subpixel. Used by status text paints.</summary>
        public static SKFont FontMedium13 { get; } = new(CustomFonts.SKFontFamilyMedium, 13) { Subpixel = true };

        /// <summary>Medium typeface, 16pt, subpixel. Used by magazine info ESP.</summary>
        public static SKFont FontMedium16 { get; } = new(CustomFonts.SKFontFamilyMedium, 16) { Subpixel = true };

        /// <summary>Medium typeface, 18pt, subpixel. Used by pulsing asterisk ESP.</summary>
        public static SKFont FontMedium18 { get; } = new(CustomFonts.SKFontFamilyMedium, 18) { Subpixel = true };

        /// <summary>Regular typeface, 48pt, subpixel. Used by TextRadarStatus.</summary>
        public static SKFont FontRegular48 { get; } = new(CustomFonts.SKFontFamilyRegular, 48) { Subpixel = true };

        /// <summary>Bold typeface, 42pt, subpixel. Used by TextMagazineESP.</summary>
        public static SKFont FontBold42 { get; } = new(CustomFonts.SKFontFamilyBold, 42) { Subpixel = true };

        /// <summary>Italic typeface, 16pt, subpixel. Used by TextMagazineInfoESP.</summary>
        public static SKFont FontItalic16 { get; } = new(CustomFonts.SKFontFamilyItalic, 16) { Subpixel = true };

        /// <summary>Default typeface, 24pt, emboldened. Used by pulsing asterisk paints.</summary>
        public static SKFont FontEmbolden24 { get; } = new() { Size = 24, Embolden = true };

        /// <summary>Default typeface, 12pt. Used by PhysicsTextPaint.</summary>
        public static SKFont FontDefault12 { get; } = new() { Size = 12 };

        // ── Mutable ESP fonts (rescaled by ScaleESPFonts) ──

        /// <summary>Medium typeface, 12pt base. Most ESP text paints.</summary>
        public static SKFont ESPFontMedium12 { get; } = new(CustomFonts.SKFontFamilyMedium, 12) { Subpixel = true };

        /// <summary>Medium typeface, 11pt base. Container loot ESP text.</summary>
        public static SKFont ESPFontMedium11 { get; } = new(CustomFonts.SKFontFamilyMedium, 11) { Subpixel = true };

        /// <summary>Medium typeface, 13pt base. Status, explosive, closest player, top loot ESP text.</summary>
        public static SKFont ESPFontMedium13 { get; } = new(CustomFonts.SKFontFamilyMedium, 13) { Subpixel = true };

        /// <summary>Medium typeface, 18pt base. Pulsing asterisk ESP text.</summary>
        public static SKFont ESPFontMedium18 { get; } = new(CustomFonts.SKFontFamilyMedium, 18) { Subpixel = true };

        /// <summary>Bold typeface, 42pt base. Magazine counter ESP text.</summary>
        public static SKFont ESPFontBold42 { get; } = new(CustomFonts.SKFontFamilyBold, 42) { Subpixel = true };

        /// <summary>Italic typeface, 16pt base. Magazine info ESP text.</summary>
        public static SKFont ESPFontItalic16 { get; } = new(CustomFonts.SKFontFamilyItalic, 16) { Subpixel = true };

        // ── Mutable Radar fonts (rescaled by GeneralSettings) ──

        /// <summary>Regular typeface, 12pt base. Most radar text paints.</summary>
        public static SKFont RadarFontRegular12 { get; } = new(CustomFonts.SKFontFamilyRegular, 12) { Subpixel = true };

        /// <summary>Regular typeface, 48pt base. Radar status text.</summary>
        public static SKFont RadarFontRegular48 { get; } = new(CustomFonts.SKFontFamilyRegular, 48) { Subpixel = true };

        /// <summary>Medium typeface, 13pt base. Radar status small text.</summary>
        public static SKFont RadarFontMedium13 { get; } = new(CustomFonts.SKFontFamilyMedium, 13) { Subpixel = true };

        /// <summary>Default typeface, 24pt, emboldened base. Radar pulsing asterisk.</summary>
        public static SKFont RadarFontEmbolden24 { get; } = new() { Size = 24, Embolden = true };

        #endregion

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
        };

        public static SKPaint PaintMouseoverGroup { get; } = new()
        {
            Color = SKColors.LawnGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
                
        public static SKPaint TextMouseoverGroup { get; } = new()
        {
            Color = SKColors.LawnGreen,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextLocalPlayer { get; } = new()
        {
            Color = SKColors.Green,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextTeammate { get; } = new()
        {
            Color = SKColors.LimeGreen,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniUSEC { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextUSEC { get; } = new()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextBEAR { get; } = new()
        {
            Color = SKColors.Blue,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextSpecial { get; } = new()
        {
            Color = SKColors.HotPink,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextStreamer { get; } = new()
        {
            Color = SKColors.MediumPurple,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextAimbotLocked { get; } = new()
        {
            Color = SKColors.Blue,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniScav { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextScav { get; } = new()
        {
            Color = SKColors.Yellow,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextRaider { get; } = new()
        {
            Color = SKColor.Parse("ffc70f"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextBoss { get; } = new()
        {
            Color = SKColors.Fuchsia,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniFocused { get; } = new()
        {
            Color = SKColors.Coral,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextFocused { get; } = new()
        {
            Color = SKColors.Coral,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniPScav { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextPScav { get; } = new() // Player Scav Text , Tooltip Text
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextMouseover { get; } = new() // Tooltip Text
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDeathMarker { get; } = new()
        {
            Color = SKColors.Black,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #endregion

        #region Loot Paints
        public static SKPaint PaintLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintImportantLoot { get; } = new()
        {
            Color = SKColors.Turquoise,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniImportantLoot { get; } = new()
        {
            Color = SKColors.Turquoise,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintContainerLoot { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniContainerLoot { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextContainer { get; } = new()
        {
            Color = SKColor.Parse("FFFFCC"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextLoot { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextImportantLoot { get; } = new()
        {
            Color = SKColors.Turquoise,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextAirdrop { get; } = new()
        {
            Color = SKColors.YellowGreen,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintAirdrop { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniAirdrop { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintCorpse { get; } = new()
        {
            Color = SKColors.Silver,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniCorpse { get; } = new()
        {
            Color = SKColors.Silver,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextCorpse { get; } = new()
        {
            Color = SKColors.Silver,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextMeds { get; } = new()
        {
            Color = SKColors.LightSalmon,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintFood { get; } = new()
        {
            Color = SKColors.CornflowerBlue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniFood { get; } = new()
        {
            Color = SKColors.CornflowerBlue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextFood { get; } = new()
        {
            Color = SKColors.CornflowerBlue,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextBackpacks { get; } = new()
        {
            Color = SKColor.Parse("00b02c"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintWeapons { get; } = new()
        {
            Color = SKColor.Parse("ffa756"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniWeapons{ get; } = new()
        {
            Color = SKColor.Parse("ffa756"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextWeapons { get; } = new()
        {
            Color = SKColor.Parse("ffa756"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint QuestHelperPaint { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint MiniQuestHelperPaint { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint QuestHelperText { get; } = new()
        {
            Color = SKColors.DeepPink,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint QuestHelperOutline { get; } = new()
        {
            Color = SKColors.DeepPink,
            StrokeWidth = 2.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintQuestItem { get; } = new()
        {
            Color = SKColors.YellowGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniQuestItem { get; } = new()
        {
            Color = SKColors.YellowGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextQuestItem { get; } = new()
        {
            Color = SKColors.YellowGreen,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintWishlistItem { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniWishlistItem { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextWishlistItem { get; } = new()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintHideoutItem { get; } = new()
        {
            Color = SKColor.Parse("00BCD4"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniHideoutItem { get; } = new()
        {
            Color = SKColor.Parse("00BCD4"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextHideoutItem { get; } = new()
        {
            Color = SKColor.Parse("00BCD4"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintHideoutItemESP { get; } = new()
        {
            Color = SKColor.Parse("00BCD4"),
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextHideoutItemESP { get; } = new()
        {
            Color = SKColor.Parse("00BCD4"),
            IsStroke = false,
            IsAntialias = true,
        };

        public static readonly SKPaint TextDoorOpen = new SKPaint
        {
            Color = SKColors.Green,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorOpen { get; } = new()
        {
            Color = SKColors.Green,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static readonly SKPaint TextDoorShut = new SKPaint
        {
            Color = SKColors.Orange,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorShut { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static readonly SKPaint TextDoorLocked = new SKPaint
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorLocked { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static readonly SKPaint TextDoorInteracting = new SKPaint
        {
            Color = SKColors.Blue,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorInteracting{ get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static readonly SKPaint TextDoorBreaching = new SKPaint
        {
            Color = SKColors.Yellow,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorBreaching { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextSwitch { get; } = new()
        {
            Color = SKColors.Orange,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilTransit { get; } = new()
        {
            Color = SKColors.Orange,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilInactive { get; } = new()
        {
            Color = SKColors.Gray,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilOpen { get; } = new()
        {
            Color = SKColors.LimeGreen,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilPending { get; } = new()
        {
            Color = SKColors.Yellow,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilClosed{ get; } = new()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
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
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextStatusSmall { get; } = new SKPaint()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextStatusSmallEsp { get; } = new SKPaint()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExplosives { get; } = new()
        {
            Color = SKColors.OrangeRed,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintExplosivesDanger { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextExplosives { get; } = new()
        {
            Color = SKColors.OrangeRed,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExplosivesDanger { get; } = new()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilOpen { get; } = new()
        {
            Color = SKColors.LimeGreen,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilTransit { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilPending { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilClosed { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilInactive { get; } = new()
        {
            Color = SKColors.Gray,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintSwitch { get; } = new()
        {
            Color = SKColors.Orange,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextOutline { get; } = new()
        {
            IsAntialias = true,
            Color = SKColors.Black,
            IsStroke = true,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
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
        };

        public static SKPaint TextPulsingAsterisk { get; } = new()
        {
            Color = SKColors.Red, // Initial color, will be updated
            IsAntialias = true,
        };

        public static SKPaint TextPulsingAsteriskOutline { get; } = new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
        };

        public static SKPaint TextPulsingAsteriskESP { get; } = new()
        {
            Color = SKColors.Red, // Initial color, will be updated
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextPulsingAsteriskOutlineESP { get; } = new()
        {
            Color = SKColors.Black,
            IsStroke = false,
            IsAntialias = true,
        };

        #endregion

        #region ESP Paints
        public static SKPaint PaintVisible { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextVisible { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };
        public static SKPaint PaintUSECESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextUSECESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBEARESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextBEARESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextBEARESPAligned { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintScavESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextScavESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintRaiderESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextRaiderESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBossESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextBossESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintAimbotLockedESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };
        public static SKPaint PhysicsTextPaint { get; } = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
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
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintFocusedESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextFocusedESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintStreamerESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextStreamerESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintSpecialESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextSpecialESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintPlayerScavESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextPlayerScavESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintFriendlyESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextFriendlyESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextLootESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintCorpseESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextCorpseESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintImpLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextImpLootESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintAirdropESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextAirdropESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintContainerLootESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextContainerLootESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintMedsESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextMedsESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintFoodESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextFoodESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintBackpackESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextBackpackESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintWeaponsESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextWeaponsESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };


        public static SKPaint PaintQuestItemESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextQuestItemESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintWishlistItemESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextWishlistItemESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintQuestHelperESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextQuestHelperESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExplosiveESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintExplosiveRadiusESP { get; } = new()
        {
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        public static SKPaint TextExplosiveESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextExfilOpenESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilOpenESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextExfilPendingESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilPendingESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextExfilClosedESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilClosedESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextExfilInactiveESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilInactiveESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextExfilTransitESP { get; } = new()
        {
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintExfilTransitESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintSwitchESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextSwitchesESP { get; } = new()
        {
            Color = SKColors.Orange,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorOpenESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextDoorOpenESP { get; } = new()
        {
            Color = SKColors.Green,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorShutESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextDoorShutESP { get; } = new()
        {
            Color = SKColors.Orange,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorLockedESP { get; } = new()
        {
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextDoorLockedESP { get; } = new()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorInteractingESP { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextDoorInteractingESP { get; } = new()
        {
            Color = SKColors.Blue,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintDoorBreachingESP { get; } = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 0.25f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint TextDoorBreachingESP { get; } = new()
        {
            Color = SKColors.Yellow,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintESPHealthBar = new()
        {
            Color = SKColors.Green,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintESPHealthBarBg = new()
        {
            Color = new SKColor(30, 30, 30, 200),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintESPHealthBarBorder = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #region ESP Readonly Paints

        public static SKPaint PaintCrosshairESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1.75f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintCrosshairESPDot { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1.75f,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintHighAlertAimlineESP { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintHighAlertBorderESP { get; } = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 3f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintBasicESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextBasicESP { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextBasicESPLeftAligned { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextESPFPS { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextESPRaidStats { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextESPStatusText { get; } = new SKPaint()
        {
            Color = SKColors.Red,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextMagazineESP { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextMagazineInfoESP { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextEnergyHydrationBarESP { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextEnergyHydrationBarOutlineESP { get; } = new()
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        public static SKPaint PaintEnergyFillESP { get; } = new()
        {
            Color = SKColor.Parse("#D4C48A"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintHydrationFillESP { get; } = new()
        {
            Color = SKColor.Parse("#5B9BD5"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintEnergyHydrationBackgroundESP { get; } = new()
        {
            Color = SKColors.Black.WithAlpha(180),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        public static SKPaint PaintFireportAimESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintAimbotFOVESP { get; } = new()
        {
            Color = SKColors.White,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextESPClosestPlayer { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextESPTopLoot { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint TextOverridePlayerESP { get; } = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true,
        };

        public static SKPaint PaintAimbotLockedLineESP { get; } = new()
        {
            Color = SKColors.Blue,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniRadarOutlineESP { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 2,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint PaintMiniRadarResizeHandleESP { get; } = new()
        {
            Color = SKColors.WhiteSmoke,
            StrokeWidth = 2,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        #endregion

        #endregion
    }
}
