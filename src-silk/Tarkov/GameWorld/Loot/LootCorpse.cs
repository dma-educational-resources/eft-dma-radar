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

        // Stroke paint for the X — color set once at init
        private static readonly SKPaint _xStroke = new()
        {
            Color = SKPaints.PaintCorpse.Color,
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

        public LootCorpse(ulong interactiveClass, Vector3 position)
        {
            InteractiveClass = interactiveClass;
            Position = position;
        }

        /// <summary>
        /// Draw this corpse on the radar canvas as an X marker.
        /// Uses direct line draws to avoid canvas Save/Translate/Restore.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos)
        {
            const float s = 4.5f;
            float px = screenPos.X, py = screenPos.Y;
            canvas.DrawLine(px - s, py - s, px + s, py + s, _xOutline);
            canvas.DrawLine(px - s, py + s, px + s, py - s, _xOutline);
            canvas.DrawLine(px - s, py - s, px + s, py + s, _xStroke);
            canvas.DrawLine(px - s, py + s, px + s, py - s, _xStroke);
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
