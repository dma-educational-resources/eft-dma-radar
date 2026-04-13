namespace eft_dma_radar.Silk.UI
{
    /// <summary>
    /// Shared SkiaSharp paint instances for the Silk radar.
    /// Contains only the paints/fonts needed by the Silk project.
    /// </summary>
    internal static class SKPaints
    {
        #region Fonts

        public static SKFont FontRegular11 { get; } = new(CustomFonts.Regular, 11) { Subpixel = true };
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

        public static SKPaint TextRadarStatus { get; } = NewTextPaint(new SKColor(77, 192, 181));

        /// <summary>Subtitle text on the idle/loading screen — dim grey.</summary>
        public static SKPaint TextRadarStatusSub { get; } = NewTextPaint(new SKColor(130, 135, 145));

        /// <summary>Font for status subtitle (smaller than title).</summary>
        public static SKFont FontRegular18 { get; } = new(CustomFonts.Regular, 18) { Subpixel = true };

        #endregion

        #region Loot Paints

        /// <summary>Normal loot — white circle + text.</summary>
        public static SKPaint LootNormal { get; } = NewTextPaint(new SKColor(200, 200, 200, 200));

        /// <summary>Valuable loot — bright green circle + text.</summary>
        public static SKPaint LootImportant { get; } = NewTextPaint(new SKColor(50, 255, 50));

        /// <summary>Wishlisted loot — bright cyan circle + text.</summary>
        public static SKPaint LootWishlist { get; } = NewTextPaint(new SKColor(0, 230, 255));

        /// <summary>Normal loot on a different floor — dimmed.</summary>
        public static SKPaint LootNormalDimmed { get; } = NewTextPaint(new SKColor(200, 200, 200, 80));

        /// <summary>Valuable loot on a different floor — dimmed.</summary>
        public static SKPaint LootImportantDimmed { get; } = NewTextPaint(new SKColor(50, 255, 50, 100));

        /// <summary>Wishlisted loot on a different floor — dimmed.</summary>
        public static SKPaint LootWishlistDimmed { get; } = NewTextPaint(new SKColor(0, 230, 255, 100));

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

        #region Transit Paints

        /// <summary>Transit point — cyan/teal.</summary>
        public static SKPaint PaintTransit { get; } = NewFillPaint(new SKColor(0, 200, 220));
        public static SKPaint TextTransit { get; } = NewTextPaint(new SKColor(0, 200, 220));

        /// <summary>Transit point inactive — dimmed.</summary>
        public static SKPaint PaintTransitInactive { get; } = NewFillPaint(new SKColor(0, 200, 220, 100));
        public static SKPaint TextTransitInactive { get; } = NewTextPaint(new SKColor(0, 200, 220, 100));

        #endregion

        #region Door Paints

        /// <summary>Locked door — red.</summary>
        public static SKPaint PaintDoorLocked { get; } = NewFillPaint(new SKColor(220, 60, 60));
        public static SKPaint TextDoorLocked { get; } = NewTextPaint(new SKColor(220, 60, 60));

        /// <summary>Open door — green.</summary>
        public static SKPaint PaintDoorOpen { get; } = NewFillPaint(new SKColor(60, 200, 60));
        public static SKPaint TextDoorOpen { get; } = NewTextPaint(new SKColor(60, 200, 60));

        /// <summary>Shut (closed but unlocked) door — orange.</summary>
        public static SKPaint PaintDoorShut { get; } = NewFillPaint(new SKColor(240, 165, 30));
        public static SKPaint TextDoorShut { get; } = NewTextPaint(new SKColor(240, 165, 30));

        /// <summary>Someone is interacting with the door.</summary>
        public static SKPaint PaintDoorInteracting { get; } = NewFillPaint(new SKColor(255, 215, 0));
        public static SKPaint TextDoorInteracting { get; } = NewTextPaint(new SKColor(255, 215, 0));

        /// <summary>Door is being breached.</summary>
        public static SKPaint PaintDoorBreaching { get; } = NewFillPaint(new SKColor(255, 100, 100));
        public static SKPaint TextDoorBreaching { get; } = NewTextPaint(new SKColor(255, 100, 100));

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

        /// <summary>Wishlist highlight text inside tooltips — cyan.</summary>
        public static SKPaint TooltipWishlist { get; } = NewTextPaint(new SKColor(0, 230, 255));

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

        /// <summary>Alias — text and fill paints use the same style.</summary>
        private static SKPaint NewTextPaint(SKColor color) => NewFillPaint(color);

        private static SKPaint NewChevronStroke(SKColor color) => new()
        {
            Color = color,
            StrokeWidth = 1.8f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };

        private static SKPaint NewAimlineStroke(SKColor color) => new()
        {
            Color = color,
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        #endregion

        #region Per-Type Stroke Paints

        // Chevron strokes — one per player type, never mutated at draw time
        public static SKPaint ChevronLocalPlayer { get; } = NewChevronStroke(new SKColor(50, 205, 50));
        public static SKPaint ChevronTeammate { get; } = NewChevronStroke(new SKColor(80, 220, 80));
        public static SKPaint ChevronUSEC { get; } = NewChevronStroke(new SKColor(230, 60, 60));
        public static SKPaint ChevronBEAR { get; } = NewChevronStroke(new SKColor(70, 130, 230));
        public static SKPaint ChevronScav { get; } = NewChevronStroke(new SKColor(240, 230, 60));
        public static SKPaint ChevronRaider { get; } = NewChevronStroke(new SKColor(255, 180, 30));
        public static SKPaint ChevronBoss { get; } = NewChevronStroke(new SKColor(230, 50, 230));
        public static SKPaint ChevronPScav { get; } = NewChevronStroke(new SKColor(220, 220, 220));
        public static SKPaint ChevronSpecial { get; } = NewChevronStroke(new SKColor(255, 90, 160));
        public static SKPaint ChevronStreamer { get; } = NewChevronStroke(new SKColor(170, 120, 255));

        // Aimline strokes — one per player type, never mutated at draw time
        public static SKPaint AimlineLocalPlayer { get; } = NewAimlineStroke(new SKColor(50, 205, 50));
        public static SKPaint AimlineTeammate { get; } = NewAimlineStroke(new SKColor(80, 220, 80));
        public static SKPaint AimlineUSEC { get; } = NewAimlineStroke(new SKColor(230, 60, 60));
        public static SKPaint AimlineBEAR { get; } = NewAimlineStroke(new SKColor(70, 130, 230));
        public static SKPaint AimlineScav { get; } = NewAimlineStroke(new SKColor(240, 230, 60));
        public static SKPaint AimlineRaider { get; } = NewAimlineStroke(new SKColor(255, 180, 30));
        public static SKPaint AimlineBoss { get; } = NewAimlineStroke(new SKColor(230, 50, 230));
        public static SKPaint AimlinePScav { get; } = NewAimlineStroke(new SKColor(220, 220, 220));
        public static SKPaint AimlineSpecial { get; } = NewAimlineStroke(new SKColor(255, 90, 160));
        public static SKPaint AimlineStreamer { get; } = NewAimlineStroke(new SKColor(170, 120, 255));

        #endregion
    }
}
