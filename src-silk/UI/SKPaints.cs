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
        /// </summary>
        public static SKPaint TextOutline { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 200),
            IsStroke = true,
            StrokeWidth = 1.6f,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

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
            IsStroke = false,
            IsAntialias = true,
        };

        #endregion
    }
}
