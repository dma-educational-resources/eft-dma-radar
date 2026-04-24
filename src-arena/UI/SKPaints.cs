namespace eft_dma_radar.Arena.UI
{
    /// <summary>
    /// Minimal shared SkiaSharp paints/fonts for the Arena radar.
    /// Only the paints currently needed are defined — ESP/loot/exfil/etc.
    /// are intentionally omitted until the corresponding systems are ported.
    /// </summary>
    internal static class SKPaints
    {
        #region Fonts

        public static SKFont FontRegular11 { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };
        public static SKFont FontRegular13 { get; } = new(CustomFonts.Regular, 13) { Subpixel = true };
        public static SKFont FontRegular18 { get; } = new(CustomFonts.Regular, 18) { Subpixel = true };
        public static SKFont FontRegular48 { get; } = new(CustomFonts.Regular, 48) { Subpixel = true };

        #endregion

        #region Text

        public static SKPaint TextShadow { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 200),
            IsAntialias = true,
        };

        public static SKPaint TextWhite { get; } = NewTextPaint(new SKColor(235, 237, 240));
        public static SKPaint TextDim   { get; } = NewTextPaint(new SKColor(150, 152, 156));

        /// <summary>Big centered status text ("Waiting for Match Start" etc).</summary>
        public static SKPaint TextRadarStatus { get; } = NewTextPaint(new SKColor(77, 192, 181));

        #endregion

        #region Player Dots / Arrows

        public static SKPaint ShapeBorder { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 180),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        // Fills
        public static SKPaint PaintLocalPlayer { get; } = NewFillPaint(new SKColor(50, 205, 50));
        public static SKPaint PaintUSEC        { get; } = NewFillPaint(new SKColor(230, 60, 60));
        public static SKPaint PaintBEAR        { get; } = NewFillPaint(new SKColor(70, 130, 230));
        public static SKPaint PaintPScav       { get; } = NewFillPaint(new SKColor(220, 220, 220));
        public static SKPaint PaintScav        { get; } = NewFillPaint(new SKColor(240, 230, 60));
        public static SKPaint PaintRaider      { get; } = NewFillPaint(new SKColor(255, 180, 30));
        public static SKPaint PaintBoss        { get; } = NewFillPaint(new SKColor(230, 50, 230));
        public static SKPaint PaintGuard       { get; } = NewFillPaint(new SKColor(200, 140, 60));
        public static SKPaint PaintDefault     { get; } = NewFillPaint(new SKColor(200, 200, 200));

        // Text colors (match fills, used for labels)
        public static SKPaint TextLocalPlayer { get; } = NewTextPaint(new SKColor(50, 205, 50));
        public static SKPaint TextUSEC        { get; } = NewTextPaint(new SKColor(230, 60, 60));
        public static SKPaint TextBEAR        { get; } = NewTextPaint(new SKColor(70, 130, 230));
        public static SKPaint TextPScav       { get; } = NewTextPaint(new SKColor(220, 220, 220));
        public static SKPaint TextScav        { get; } = NewTextPaint(new SKColor(240, 230, 60));
        public static SKPaint TextRaider      { get; } = NewTextPaint(new SKColor(255, 180, 30));
        public static SKPaint TextBoss        { get; } = NewTextPaint(new SKColor(230, 50, 230));
        public static SKPaint TextGuard       { get; } = NewTextPaint(new SKColor(200, 140, 60));

        // Aimline strokes (shared thin line)
        public static SKPaint Aimline { get; } = new()
        {
            Color = new SKColor(235, 237, 240, 180),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        #endregion

        #region Grid (fallback when no map)

        public static SKPaint GridMinor { get; } = new()
        {
            Color = new SKColor(40, 40, 48),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false,
        };

        public static SKPaint GridMajor { get; } = new()
        {
            Color = new SKColor(70, 70, 80),
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = false,
        };

        #endregion

        #region Helpers

        private static SKPaint NewFillPaint(SKColor color) => new()
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        private static SKPaint NewTextPaint(SKColor color) => NewFillPaint(color);

        #endregion
    }
}
