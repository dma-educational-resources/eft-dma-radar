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
        public static SKFont FontRegular48 { get; } = new(CustomFonts.Regular, 48) { Subpixel = true };

        #endregion

        #region Player Paints

        public static SKPaint PaintLocalPlayer { get; } = NewStrokePaint(SKColors.Green);
        public static SKPaint TextLocalPlayer { get; } = NewTextPaint(SKColors.Green);

        public static SKPaint PaintTeammate { get; } = NewStrokePaint(SKColors.LimeGreen);
        public static SKPaint TextTeammate { get; } = NewTextPaint(SKColors.LimeGreen);

        public static SKPaint PaintUSEC { get; } = NewStrokePaint(SKColors.Red);
        public static SKPaint TextUSEC { get; } = NewTextPaint(SKColors.Red);

        public static SKPaint PaintBEAR { get; } = NewStrokePaint(SKColors.Blue);
        public static SKPaint TextBEAR { get; } = NewTextPaint(SKColors.Blue);

        public static SKPaint PaintScav { get; } = NewStrokePaint(SKColors.Yellow);
        public static SKPaint TextScav { get; } = NewTextPaint(SKColors.Yellow);

        public static SKPaint PaintRaider { get; } = NewStrokePaint(SKColor.Parse("ffc70f"));
        public static SKPaint TextRaider { get; } = NewTextPaint(SKColor.Parse("ffc70f"));

        public static SKPaint PaintBoss { get; } = NewStrokePaint(SKColors.Fuchsia);
        public static SKPaint TextBoss { get; } = NewTextPaint(SKColors.Fuchsia);

        public static SKPaint PaintPScav { get; } = NewStrokePaint(SKColors.White);
        public static SKPaint TextPScav { get; } = NewTextPaint(SKColors.White);

        public static SKPaint PaintSpecial { get; } = NewStrokePaint(SKColors.HotPink);
        public static SKPaint TextSpecial { get; } = NewTextPaint(SKColors.HotPink);

        public static SKPaint PaintStreamer { get; } = NewStrokePaint(SKColors.MediumPurple);
        public static SKPaint TextStreamer { get; } = NewTextPaint(SKColors.MediumPurple);

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

        #region Pulsing Asterisk

        private static readonly Stopwatch _pulseTimer = Stopwatch.StartNew();

        public static void UpdatePulsingAsteriskColor()
        {
            var time = _pulseTimer.ElapsedMilliseconds / 1000.0;
            var pulseFactor = (Math.Sin(time * 4) + 1) / 2;
            var greenValue = (byte)(pulseFactor * 100);
            // Future: apply to pulsing paint if needed
        }

        #endregion

        #region Helpers

        private static SKPaint NewStrokePaint(SKColor color) => new()
        {
            Color = color,
            StrokeWidth = 3,
            Style = SKPaintStyle.Stroke,
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
