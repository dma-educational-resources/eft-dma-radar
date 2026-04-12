using eft_dma_radar.Silk.Misc.Data;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// A single loose loot item on the ground with a map position.
    /// All filter/visibility decisions are delegated to <see cref="LootFilter"/>.
    /// </summary>
    internal sealed class LootItem
    {
        private readonly TarkovMarketItem _item;

        // Cached label to avoid per-frame string allocation
        private string? _cachedLabel;
        private int _cachedLabelPrice = -1;

        public string Id { get; }
        public string Name => _item.Name;
        public string ShortName => _item.ShortName;
        public Vector3 Position { get; set; }

        /// <summary>Effective display price (respects price source + price-per-slot).</summary>
        public int DisplayPrice => LootFilter.GetDisplayPrice(_item);

        /// <summary>Whether the item passes current filter criteria.</summary>
        public bool ShouldDraw() => LootFilter.ShouldDraw(_item, DisplayPrice);

        /// <summary>Whether the item passes current filter criteria (pre-computed price).</summary>
        public bool ShouldDraw(int displayPrice) => LootFilter.ShouldDraw(_item, displayPrice);

        /// <summary>Whether the item is highlighted as important.</summary>
        public bool IsImportant => LootFilter.IsImportant(DisplayPrice);

        public LootItem(TarkovMarketItem item, Vector3 position)
        {
            _item = item;
            Id = item.BsgId;
            Position = position;
        }

        /// <summary>
        /// Draw this loot item on the radar canvas.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos)
        {
            Draw(canvas, screenPos, DisplayPrice);
        }

        /// <summary>
        /// Draw this loot item on the radar canvas (pre-computed price).
        /// When <paramref name="differentFloor"/> is true the item is dimmed with a
        /// [!] prefix to signal it is likely under the map and inaccessible.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, int price, bool differentFloor = false)
        {
            bool important = LootFilter.IsImportant(price);
            var paint = (important, differentFloor) switch
            {
                (true,  false) => SKPaints.LootImportant,
                (true,  true)  => SKPaints.LootImportantDimmed,
                (false, false) => SKPaints.LootNormal,
                (false, true)  => SKPaints.LootNormalDimmed,
            };
            canvas.DrawCircle(screenPos, differentFloor ? 3f : 4f, paint);

            // Cache label string — only regenerate when price or floor state changes
            int cacheKey = differentFloor ? ~price : price; // Flip bits to distinguish floor state
            if (cacheKey != _cachedLabelPrice || _cachedLabel is null)
            {
                _cachedLabelPrice = cacheKey;
                string baseLabel = price > 0 ? $"{ShortName} ({FormatPrice(price)})" : ShortName;
                _cachedLabel = differentFloor ? $"[!] {baseLabel}" : baseLabel;
            }

            float lx = screenPos.X + 7;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText(_cachedLabel, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.LootShadow);
            canvas.DrawText(_cachedLabel, lx, ly, SKPaints.FontRegular11, paint);
        }

        private static string FormatPrice(int price) => LootFilter.FormatPrice(price);
    }
}
