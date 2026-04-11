using System.Collections.Frozen;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// A corpse on the ground with a map position, optional name from dogtag,
    /// and equipment items read from its inventory.
    /// </summary>
    internal sealed class LootCorpse
    {
        /// <summary>InteractiveClass address — used to correlate with dogtag data.</summary>
        public ulong InteractiveClass { get; }

        /// <summary>Display name (victim nickname if resolved, otherwise "Corpse").</summary>
        public string Name { get; set; } = "Corpse";

        /// <summary>World position of the corpse.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Equipment items on the corpse (slot → item info). Empty until read.</summary>
        public FrozenDictionary<string, CorpseGearItem> Equipment { get; set; } =
            FrozenDictionary<string, CorpseGearItem>.Empty;

        /// <summary>Total estimated value of all equipment on the corpse.</summary>
        public int TotalValue { get; set; }

        /// <summary>Whether equipment has been read at least once.</summary>
        public bool GearReady { get; set; }

        // Pre-built X marker path — static, shared across all corpses
        private static readonly SKPath _xMarker = CreateXMarker();

        private static SKPath CreateXMarker()
        {
            const float s = 4.5f;
            var path = new SKPath();
            path.MoveTo(-s, -s);
            path.LineTo(s, s);
            path.MoveTo(-s, s);
            path.LineTo(s, -s);
            return path;
        }

        // Stroke paint for the X — reused, color set per-draw
        private static readonly SKPaint _xStroke = new()
        {
            StrokeWidth = 2.0f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        private static readonly SKPaint _xOutline = new()
        {
            Color = new SKColor(0, 0, 0, 160),
            StrokeWidth = 3.4f,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        // Cached label to avoid per-frame string allocation
        private string? _cachedLabel;
        private string? _cachedLabelName;
        private int _cachedLabelValue = -1;

        public LootCorpse(ulong interactiveClass, Vector3 position)
        {
            InteractiveClass = interactiveClass;
            Position = position;
        }

        /// <summary>
        /// Draw this corpse on the radar canvas as an X marker with name label.
        /// </summary>
        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player.Player localPlayer)
        {
            var mapPos = MapParams.ToMapPos(Position, mapConfig);
            var screenPos = mapParams.ToScreenPos(mapPos);

            // X marker
            canvas.Save();
            canvas.Translate(screenPos.X, screenPos.Y);
            canvas.DrawPath(_xMarker, _xOutline);
            _xStroke.Color = SKPaints.PaintCorpse.Color;
            canvas.DrawPath(_xMarker, _xStroke);
            canvas.Restore();

            // Name label — cached to avoid per-frame allocation
            float lx = screenPos.X + 7;
            float ly = screenPos.Y + 4.5f;

            if (_cachedLabel is null || _cachedLabelValue != TotalValue || _cachedLabelName != Name)
            {
                _cachedLabelName = Name;
                _cachedLabelValue = TotalValue;
                _cachedLabel = TotalValue > 0 ? $"{Name} ({LootFilter.FormatPrice(TotalValue)})" : Name;
            }

            canvas.DrawText(_cachedLabel, lx + 1, ly + 1, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.LootShadow);
            canvas.DrawText(_cachedLabel, lx, ly, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.TextCorpse);
        }
    }

    /// <summary>
    /// A single equipment item found on a corpse.
    /// </summary>
    internal sealed class CorpseGearItem
    {
        public required string ShortName { get; init; }
        public required string Name { get; init; }
        public int Price { get; init; }
    }
}
