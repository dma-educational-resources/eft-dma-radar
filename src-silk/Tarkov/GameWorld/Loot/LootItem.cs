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
        private int _cachedLabelKey = int.MinValue;

        public string Id { get; }
        public string Name => _item.Name;
        public string ShortName => _item.ShortName;
        public Vector3 Position { get; set; }

        /// <summary>The underlying market item data.</summary>
        public TarkovMarketItem MarketItem => _item;

        /// <summary>Effective display price (respects price source + price-per-slot).</summary>
        public int DisplayPrice => LootFilter.GetDisplayPrice(_item);

        /// <summary>Full filter evaluation — visibility, importance, wishlist, category.</summary>
        public LootFilter.FilterResult Evaluate(int displayPrice) => LootFilter.Evaluate(_item, displayPrice);

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
        /// Draw this loot item on the radar canvas with full filter result.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, int price, LootFilter.FilterResult result, bool differentFloor = false)
        {
            var paint = GetPaint(result, differentFloor);
            canvas.DrawCircle(screenPos, differentFloor ? 3f : 4f, paint);

            // Cache label string — encode state into key
            int labelKey = HashCode.Combine(price, differentFloor, result.Wishlisted, result.CategoryMatch);
            if (labelKey != _cachedLabelKey || _cachedLabel is null)
            {
                _cachedLabelKey = labelKey;
                string prefix = differentFloor ? "[!] " : "";
                string suffix = result.Wishlisted ? " \u2605" : result.CategoryMatch ? " \u25cf" : "";
                string baseLabel = price > 0 ? $"{ShortName} ({FormatPrice(price)})" : ShortName;
                _cachedLabel = $"{prefix}{baseLabel}{suffix}";
            }

            float lx = screenPos.X + 7;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText(_cachedLabel, lx + 1, ly + 1, SKPaints.FontRegular11, SKPaints.LootShadow);
            canvas.DrawText(_cachedLabel, lx, ly, SKPaints.FontRegular11, paint);
        }

        /// <summary>
        /// Draw this loot item on the radar canvas (pre-computed price, legacy overload).
        /// When <paramref name="differentFloor"/> is true the item is dimmed with a
        /// [!] prefix to signal it is likely under the map and inaccessible.
        /// </summary>
        public void Draw(SKCanvas canvas, SKPoint screenPos, int price, bool differentFloor = false)
        {
            var result = Evaluate(price);
            Draw(canvas, screenPos, price, result, differentFloor);
        }

        private static SKPaint GetPaint(LootFilter.FilterResult result, bool differentFloor)
        {
            if (result.Wishlisted)
                return differentFloor ? SKPaints.LootWishlistDimmed : SKPaints.LootWishlist;

            if (result.Important)
                return differentFloor ? SKPaints.LootImportantDimmed : SKPaints.LootImportant;

            return differentFloor ? SKPaints.LootNormalDimmed : SKPaints.LootNormal;
        }

        private static string FormatPrice(int price) => LootFilter.FormatPrice(price);
    }
}
