namespace eft_dma_radar.Silk.UI.ESP
{
    /// <summary>
    /// Cached SkiaSharp paint instances for ESP rendering.
    /// All instances are pre-allocated — never create paints in the render loop.
    /// </summary>
    internal static class EspPaints
    {
        #region Fonts

        public static SKFont FontName { get; } = new(CustomFonts.Regular, 12) { Subpixel = true };
        public static SKFont FontInfo { get; } = new(CustomFonts.Regular, 10) { Subpixel = true };
        public static SKFont FontLoot { get; } = new(CustomFonts.Regular, 10) { Subpixel = true };

        #endregion

        #region Text Shadow

        public static SKPaint TextShadow { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 200),
            IsStroke = false,
            IsAntialias = true,
        };

        #endregion

        #region Box Outline

        public static SKPaint BoxOutline { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            StrokeWidth = 3f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #endregion

        #region Health Bar

        public static SKPaint HealthBarBg { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 140),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthGreen { get; } = new()
        {
            Color = new SKColor(50, 200, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthYellow { get; } = new()
        {
            Color = new SKColor(220, 200, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HealthRed { get; } = new()
        {
            Color = new SKColor(220, 50, 50, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        #endregion

        #region Player Type — Box + Text

        // USEC
        public static SKPaint BoxUSEC { get; } = MakeBoxPaint(230, 60, 60);
        public static SKPaint TextUSEC { get; } = MakeFillPaint(230, 60, 60);

        // BEAR
        public static SKPaint BoxBEAR { get; } = MakeBoxPaint(70, 130, 230);
        public static SKPaint TextBEAR { get; } = MakeFillPaint(70, 130, 230);

        // PScav
        public static SKPaint BoxPScav { get; } = MakeBoxPaint(220, 220, 50);
        public static SKPaint TextPScav { get; } = MakeFillPaint(220, 220, 50);

        // Teammate
        public static SKPaint BoxTeammate { get; } = MakeBoxPaint(80, 220, 80);
        public static SKPaint TextTeammate { get; } = MakeFillPaint(80, 220, 80);

        // AIScav
        public static SKPaint BoxScav { get; } = MakeBoxPaint(240, 230, 60);
        public static SKPaint TextScav { get; } = MakeFillPaint(240, 230, 60);

        // AIRaider
        public static SKPaint BoxRaider { get; } = MakeBoxPaint(255, 180, 30);
        public static SKPaint TextRaider { get; } = MakeFillPaint(255, 180, 30);

        // AIBoss
        public static SKPaint BoxBoss { get; } = MakeBoxPaint(230, 50, 230);
        public static SKPaint TextBoss { get; } = MakeFillPaint(230, 50, 230);

        // Special
        public static SKPaint BoxSpecial { get; } = MakeBoxPaint(255, 100, 160);
        public static SKPaint TextSpecial { get; } = MakeFillPaint(255, 100, 160);

        // Streamer
        public static SKPaint BoxStreamer { get; } = MakeBoxPaint(170, 120, 255);
        public static SKPaint TextStreamer { get; } = MakeFillPaint(170, 120, 255);

        // Default / LocalPlayer
        public static SKPaint BoxDefault { get; } = MakeBoxPaint(200, 200, 200);
        public static SKPaint TextDefault { get; } = MakeFillPaint(200, 200, 200);

        #endregion

        #region Bones

        public static SKPaint BoneLine { get; } = new()
        {
            Color = new SKColor(255, 255, 255, 200),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        #endregion

        #region Loot

        public static SKPaint TextLoot { get; } = MakeFillPaint(200, 200, 200, 210);
        public static SKPaint TextLootImportant { get; } = MakeFillPaint(50, 255, 50, 240);
        public static SKPaint TextLootWishlist { get; } = MakeFillPaint(0, 230, 255, 240);
        public static SKPaint TextLootQuest { get; } = MakeFillPaint(255, 200, 50, 240);

        #endregion

        #region Crosshair

        public static SKPaint Crosshair { get; } = new()
        {
            Color = new SKColor(255, 0, 0, 230),
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKPaint CrosshairDot { get; } = new()
        {
            Color = new SKColor(255, 0, 0, 230),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        #endregion

        #region Energy / Hydration Bars

        public static SKPaint StatusBarBg { get; } = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint EnergyFill { get; } = new()
        {
            Color = new SKColor(255, 200, 40, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint HydrationFill { get; } = new()
        {
            Color = new SKColor(60, 180, 255, 220),
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public static SKPaint StatusBarBorder { get; } = new()
        {
            Color = new SKColor(255, 255, 255, 160),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        public static SKFont FontBar { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };

        public static SKPaint TextBar { get; } = MakeFillPaint(255, 255, 255, 240);

        #endregion

        #region Status Text

        public static SKFont FontStatus { get; } = new(CustomFonts.Regular, 14) { Subpixel = true };

        public static SKPaint TextStatus { get; } = MakeFillPaint(255, 220, 60, 240);

        #endregion

        #region Helpers

        /// <summary>
        /// Returns the (box, text) paint pair for a given player type.
        /// </summary>
        public static (SKPaint box, SKPaint text) GetPlayerPaints(PlayerType type) => type switch
        {
            PlayerType.Teammate      => (BoxTeammate, TextTeammate),
            PlayerType.USEC          => (BoxUSEC, TextUSEC),
            PlayerType.BEAR          => (BoxBEAR, TextBEAR),
            PlayerType.PScav         => (BoxPScav, TextPScav),
            PlayerType.AIScav        => (BoxScav, TextScav),
            PlayerType.AIRaider      => (BoxRaider, TextRaider),
            PlayerType.AIBoss        => (BoxBoss, TextBoss),
            PlayerType.SpecialPlayer => (BoxSpecial, TextSpecial),
            PlayerType.Streamer      => (BoxStreamer, TextStreamer),
            _                        => (BoxDefault, TextDefault),
        };

        private static SKPaint MakeBoxPaint(byte r, byte g, byte b, byte a = 220) => new()
        {
            Color = new SKColor(r, g, b, a),
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
        };

        private static SKPaint MakeFillPaint(byte r, byte g, byte b, byte a = 255) => new()
        {
            Color = new SKColor(r, g, b, a),
            IsStroke = false,
            IsAntialias = true,
        };

        #endregion
    }
}
