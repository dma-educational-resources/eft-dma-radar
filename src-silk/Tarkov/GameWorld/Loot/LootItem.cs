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

        public string Id { get; }
        public string Name => _item.Name;
        public string ShortName => _item.ShortName;
        public Vector3 Position { get; set; }

        /// <summary>Effective display price (respects price source + price-per-slot).</summary>
        public int DisplayPrice => LootFilter.GetDisplayPrice(_item);

        /// <summary>Whether the item passes current filter criteria.</summary>
        public bool ShouldDraw() => LootFilter.ShouldDraw(_item, DisplayPrice);

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
        public void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig, Player.Player localPlayer)
        {
            var price = DisplayPrice;

            var mapPos = MapParams.ToMapPos(Position, mapConfig);
            var screenPos = mapParams.ToScreenPos(mapPos);

            var paint = LootFilter.IsImportant(price) ? SKPaints.LootImportant : SKPaints.LootNormal;
            canvas.DrawCircle(screenPos, 4f, paint);

            string label = price > 0 ? $"{ShortName} ({FormatPrice(price)})" : ShortName;
            float lx = screenPos.X + 7;
            float ly = screenPos.Y + 4.5f;
            canvas.DrawText(label, lx, ly, SKTextAlign.Left, SKPaints.FontRegular11, SKPaints.LootShadow);
            canvas.DrawText(label, lx, ly, SKTextAlign.Left, SKPaints.FontRegular11, paint);
        }

        private static string FormatPrice(int price) => LootFilter.FormatPrice(price);
    }
}
