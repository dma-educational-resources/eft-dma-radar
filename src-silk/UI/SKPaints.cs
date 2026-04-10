namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Shared SkiaSharp paint instances for the Silk radar.
    /// Contains only the paints/fonts needed by the Silk project.
    /// </summary>
    internal static class SKPaints
    {
        #region Fonts

        public static SKFont FontRegular12 { get; } = new(CustomFonts.Regular, 12) { Subpixel = true };
        public static SKFont FontRegular11 { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };
        public static SKFont FontMedium11 { get; } = new(CustomFonts.Medium, 11) { Subpixel = true };
        public static SKFont FontRegular48 { get; } = new(CustomFonts.Regular, 48) { Subpixel = true };

        #endregion

        #region Shape/Text Outlines

        /// <summary>
        /// Thin border around filled player dot for contrast.
        /// </summary>
        public static SKPaint ShapeBorder { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 180),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        /// <summary>
        /// Subtle drop-shadow behind text labels for readability.
        /// Drawn at a small offset from the main text for a crisp shadow effect.
        /// </summary>
        public static SKPaint TextShadow { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 200),
            IsStroke = false,
            IsAntialias = true,
        };

        /// <summary>
        /// Drop-shadow behind loot text labels for readability.
        /// Same paint as <see cref="TextShadow"/> — shared to avoid duplicate allocation.
        /// </summary>
        public static SKPaint LootShadow => TextShadow;

        /// <summary>
        /// Death marker paint — small X for dead players.
        /// </summary>
        public static SKPaint PaintDeathMarker { get; } = new()
        {
            Color = new SKColor(160, 160, 160, 140),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        #endregion

        #region Player Paints

        public static SKPaint PaintLocalPlayer { get; } = NewFillPaint(new SKColor(50, 205, 50));
        public static SKPaint TextLocalPlayer { get; } = NewTextPaint(new SKColor(50, 205, 50));

        public static SKPaint PaintTeammate { get; } = NewFillPaint(new SKColor(80, 220, 80));
        public static SKPaint TextTeammate { get; } = NewTextPaint(new SKColor(80, 220, 80));

        public static SKPaint PaintUSEC { get; } = NewFillPaint(new SKColor(230, 60, 60));
        public static SKPaint TextUSEC { get; } = NewTextPaint(new SKColor(230, 60, 60));

        public static SKPaint PaintBEAR { get; } = NewFillPaint(new SKColor(70, 130, 230));
        public static SKPaint TextBEAR { get; } = NewTextPaint(new SKColor(70, 130, 230));

        public static SKPaint PaintScav { get; } = NewFillPaint(new SKColor(240, 230, 60));
        public static SKPaint TextScav { get; } = NewTextPaint(new SKColor(240, 230, 60));

        public static SKPaint PaintRaider { get; } = NewFillPaint(new SKColor(255, 180, 30));
        public static SKPaint TextRaider { get; } = NewTextPaint(new SKColor(255, 180, 30));

        public static SKPaint PaintBoss { get; } = NewFillPaint(new SKColor(230, 50, 230));
        public static SKPaint TextBoss { get; } = NewTextPaint(new SKColor(230, 50, 230));

        public static SKPaint PaintPScav { get; } = NewFillPaint(new SKColor(220, 220, 220));
        public static SKPaint TextPScav { get; } = NewTextPaint(new SKColor(220, 220, 220));

        public static SKPaint PaintSpecial { get; } = NewFillPaint(new SKColor(255, 90, 160));
        public static SKPaint TextSpecial { get; } = NewTextPaint(new SKColor(255, 90, 160));

        public static SKPaint PaintStreamer { get; } = NewFillPaint(new SKColor(170, 120, 255));
        public static SKPaint TextStreamer { get; } = NewTextPaint(new SKColor(170, 120, 255));

        #endregion

        #region Radar Paints

        public static SKPaint PaintConnectorGroup { get; } = new()
        {
            Color = SKColors.LawnGreen.WithAlpha(60),
            StrokeWidth = 2.25f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint TextRadarStatus { get; } = NewTextPaint(SKColors.Red);

        #endregion

        #region Loot Paints

        /// <summary>Normal loot — white circle + text.</summary>
        public static SKPaint LootNormal { get; } = NewTextPaint(new SKColor(200, 200, 200, 200));

        /// <summary>Valuable loot — bright green circle + text.</summary>
        public static SKPaint LootImportant { get; } = NewTextPaint(new SKColor(50, 255, 50));

        /// <summary>Corpse marker fill — muted orange.</summary>
        public static SKPaint PaintCorpse { get; } = NewFillPaint(new SKColor(200, 150, 80, 180));

        /// <summary>Corpse label text — muted orange.</summary>
        public static SKPaint TextCorpse { get; } = NewTextPaint(new SKColor(200, 150, 80, 200));

        #endregion

        #region Exfil Paints

        /// <summary>Exfil open — green.</summary>
        public static SKPaint PaintExfilOpen { get; } = NewFillPaint(new SKColor(50, 205, 50));
        public static SKPaint TextExfilOpen { get; } = NewTextPaint(new SKColor(50, 205, 50));

        /// <summary>Exfil pending — yellow.</summary>
        public static SKPaint PaintExfilPending { get; } = NewFillPaint(new SKColor(255, 215, 0));
        public static SKPaint TextExfilPending { get; } = NewTextPaint(new SKColor(255, 215, 0));

        /// <summary>Exfil closed — red.</summary>
        public static SKPaint PaintExfilClosed { get; } = NewFillPaint(new SKColor(200, 60, 60));
        public static SKPaint TextExfilClosed { get; } = NewTextPaint(new SKColor(200, 60, 60));

        /// <summary>Exfil inactive (not available for player) — dimmed grey.</summary>
        public static SKPaint PaintExfilInactive { get; } = NewFillPaint(new SKColor(120, 120, 120, 120));
        public static SKPaint TextExfilInactive { get; } = NewTextPaint(new SKColor(120, 120, 120, 120));

        #endregion

        #region Tooltip Paints

        /// <summary>Semi-transparent dark background for mouseover tooltips.</summary>
        public static SKPaint TooltipBackground { get; } = new()
        {
            Color = new SKColor(15, 15, 15, 210),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        /// <summary>Subtle border around tooltip background.</summary>
        public static SKPaint TooltipBorder { get; } = new()
        {
            Color = new SKColor(120, 120, 120, 140),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };

        /// <summary>Primary text inside tooltips.</summary>
        public static SKPaint TooltipText { get; } = NewTextPaint(new SKColor(220, 220, 220));

        /// <summary>Dimmed label text inside tooltips.</summary>
        public static SKPaint TooltipLabel { get; } = NewTextPaint(new SKColor(150, 150, 150));

        /// <summary>Accent / money value text inside tooltips.</summary>
        public static SKPaint TooltipAccent { get; } = NewTextPaint(new SKColor(100, 210, 100));

        /// <summary>Font used for tooltip text.</summary>
        public static SKFont FontTooltip { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };

        #endregion

        #region Helpers

        private static SKPaint NewFillPaint(SKColor color) => new()
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        private static SKPaint NewTextPaint(SKColor color) => new()
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        #endregion
    }
}
