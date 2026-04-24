namespace eft_dma_radar.Silk.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// A single loose loot item on the ground with a map position.
    /// All filter/visibility decisions are delegated to <see cref="LootFilter"/>.
    /// </summary>
    internal sealed class LootItem(TarkovMarketItem item, Vector3 position)
    {
        private readonly TarkovMarketItem _item = item;

        /// <summary>
        /// True when this is a loose quest item (ItemTemplate.QuestItem flag) such as
        /// pocket watches or Jaeger's letter. These don't appear in the market database
        /// and are visibility-controlled by <see cref="SilkConfig.LootShowQuestItems"/>.
        /// </summary>
        public bool IsQuestItem { get; init; }

        // Cached label to avoid per-frame string allocation
        private string? _cachedLabel;
        private int _cachedLabelKey = int.MinValue;

        // Cached importance flag — updated by LootManager after each loot refresh
        private bool _cachedImportant;

        public string Id { get; } = item.BsgId;
        public string Name => _item.Name;
        public string ShortName => _item.ShortName;
        public Vector3 Position { get; set; } = position;

        /// <summary>The underlying market item data.</summary>
        public TarkovMarketItem MarketItem => _item;

        /// <summary>Effective display price (respects price source + price-per-slot).</summary>
        public int DisplayPrice => LootFilter.GetDisplayPrice(_item);

        /// <summary>Full filter evaluation — visibility, importance, wishlist, category.</summary>
        public LootFilter.FilterResult Evaluate(int displayPrice)
        {
            if (IsQuestItem)
            {
                if (!SilkProgram.Config.LootShowQuestItems)
                    return LootFilter.FilterResult.Hidden;

                return new LootFilter.FilterResult
                {
                    Visible = true,
                    Important = true,
                    QuestRequired = true,
                };
            }
            return LootFilter.Evaluate(_item, displayPrice);
        }

        /// <summary>Whether the item passes current filter criteria.</summary>
        public bool ShouldDraw() => Evaluate(DisplayPrice).Visible;

        /// <summary>Whether the item passes current filter criteria (pre-computed price).</summary>
        public bool ShouldDraw(int displayPrice) => Evaluate(displayPrice).Visible;

        /// <summary>Whether the item is highlighted as important (cached — call <see cref="RefreshImportance"/> to update).</summary>
        public bool IsImportant => _cachedImportant;

        /// <summary>
        /// Refreshes the cached importance flag from the current price/config state.
        /// Called after loot list construction to avoid per-frame/per-door recomputation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RefreshImportance() => _cachedImportant = LootFilter.IsImportant(DisplayPrice);

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
        public void Draw(SKCanvas canvas, SKPoint screenPos, int price, LootFilter.FilterResult result, bool differentFloor = false, float heightDelta = 0f)
        {
            var paint = GetPaint(result, differentFloor);

            var cfg = SilkProgram.Config;
            float baseR = Math.Clamp(cfg.LootDotSize, 1.5f, 8f);
            // Tier-based size: base + smaller per-tier bump. Different-floor dots stay compact.
            float radius = differentFloor
                ? Math.Max(1.5f, baseR - 1f)
                : baseR + (result.Tier >= 1 ? (result.Tier - 1) * 0.4f + 0.3f : 0f);

            // Decide whether to draw a height arrow instead of the dot.
            // Mirrors WPF/Lone behavior — the marker itself becomes the arrow.
            int heightDir = 0; // -1 = below player, 0 = same floor, +1 = above
            if (cfg.LootShowHeightArrows)
            {
                float thr = Math.Max(0.3f, cfg.LootHeightArrowThreshold);
                if (heightDelta > thr) heightDir = 1;
                else if (heightDelta < -thr) heightDir = -1;
            }

            // Halo ring for rare (tier 2) and top (tier 3) items — makes them easy to pick out.
            if (!differentFloor && result.Tier >= 2)
            {
                float ringR = radius + (result.Tier == 3 ? 3f : 2f);
                canvas.DrawCircle(screenPos, ringR, SKPaints.LootHaloRing);
            }

            if (heightDir != 0)
            {
                // Triangle marker — up for above, down for below. Slightly larger than the dot
                // so floor separation stands out at a glance.
                float size = radius + 1.5f;
                using var path = BuildArrowPath(screenPos, size, heightDir > 0);
                canvas.DrawPath(path, SKPaints.LootArrowOutline);
                canvas.DrawPath(path, paint);
            }
            else
            {
                canvas.DrawCircle(screenPos, radius, paint);
            }

            // Cache label string — encode state into key
            int labelKey = HashCode.Combine(price, differentFloor, result.Wishlisted, result.CategoryMatch, result.Tier, heightDir, cfg.LootShowHeightDelta);
            if (labelKey != _cachedLabelKey || _cachedLabel is null)
            {
                _cachedLabelKey = labelKey;
                string prefix = differentFloor ? "[!] " : "";
                // Tier markers use ASCII (Neo Sans Std lacks most geometric glyphs).
                string suffix;
                if (result.Wishlisted)
                    suffix = " *";
                else if (result.Tier == 3)
                    suffix = " ++";
                else if (result.Tier == 2)
                    suffix = " +";
                else if (result.CategoryMatch)
                    suffix = " .";
                else
                    suffix = "";

                // Optional numeric height delta, e.g. "+4m" / "-2m".
                string heightTxt = "";
                if (heightDir != 0 && cfg.LootShowHeightDelta)
                    heightTxt = $" {(heightDelta >= 0 ? "+" : "")}{(int)MathF.Round(heightDelta)}m";

                string baseLabel = price > 0 ? $"{ShortName} ({FormatPrice(price)})" : ShortName;
                _cachedLabel = $"{prefix}{baseLabel}{suffix}{heightTxt}";
            }

            float lx = screenPos.X + 7;
            float ly = screenPos.Y + 4.5f;
            var font = SKPaints.GetFont(cfg.LootLabelFontSize);
            canvas.DrawText(_cachedLabel, lx + 1, ly + 1, font, SKPaints.LootShadow);
            canvas.DrawText(_cachedLabel, lx, ly, font, paint);
        }

        /// <summary>
        /// Builds a small filled-triangle <see cref="SKPath"/> centered on <paramref name="p"/>.
        /// Up-triangle when <paramref name="up"/> is true, down-triangle otherwise.
        /// </summary>
        private static SKPath BuildArrowPath(SKPoint p, float size, bool up)
        {
            var path = new SKPath();
            if (up)
            {
                path.MoveTo(p.X, p.Y - size);
                path.LineTo(p.X - size, p.Y + size * 0.8f);
                path.LineTo(p.X + size, p.Y + size * 0.8f);
            }
            else
            {
                path.MoveTo(p.X, p.Y + size);
                path.LineTo(p.X - size, p.Y - size * 0.8f);
                path.LineTo(p.X + size, p.Y - size * 0.8f);
            }
            path.Close();
            return path;
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

            // Value tiers override the plain "important green" at 2×/5× the threshold
            if (result.Tier >= 3)
                return differentFloor ? SKPaints.LootTopDimmed : SKPaints.LootTop;
            if (result.Tier == 2)
                return differentFloor ? SKPaints.LootRareDimmed : SKPaints.LootRare;

            if (result.Important)
                return differentFloor ? SKPaints.LootImportantDimmed : SKPaints.LootImportant;

            return differentFloor ? SKPaints.LootNormalDimmed : SKPaints.LootNormal;
        }

        private static string FormatPrice(int price) => LootFilter.FormatPrice(price);
    }
}
