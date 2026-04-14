using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;

namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Flattened loot item snapshot for the web radar client.
    /// </summary>
    public sealed class WebRadarLootItem
    {
        public string Name { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public int Price { get; set; }
        public bool Important { get; set; }
        public bool Wishlisted { get; set; }
        public bool QuestItem { get; set; }

        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        internal static WebRadarLootItem? Create(LootItem item)
        {
            var price = item.DisplayPrice;
            var result = item.Evaluate(price);
            if (!result.Visible)
                return null;

            var pos = item.Position;
            return new WebRadarLootItem
            {
                Name = item.Name,
                ShortName = item.ShortName,
                Price = price,
                Important = result.Important,
                Wishlisted = result.Wishlisted,
                QuestItem = result.QuestRequired,
                WorldX = pos.X,
                WorldY = pos.Y,
                WorldZ = pos.Z,
            };
        }
    }
}
