using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;

namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Flattened container snapshot for the web radar client.
    /// </summary>
    public sealed class WebRadarContainer
    {
        public string Name { get; set; } = string.Empty;
        public bool Searched { get; set; }

        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        internal static WebRadarContainer Create(LootContainer container)
        {
            var pos = container.Position;
            return new WebRadarContainer
            {
                Name = container.Name,
                Searched = container.Searched,
                WorldX = pos.X,
                WorldY = pos.Y,
                WorldZ = pos.Z,
            };
        }
    }
}
