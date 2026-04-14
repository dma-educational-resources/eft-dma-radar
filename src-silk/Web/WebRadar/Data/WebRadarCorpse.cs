using eft_dma_radar.Silk.Tarkov.GameWorld.Loot;

namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Flattened corpse snapshot for the web radar client.
    /// </summary>
    public sealed class WebRadarCorpse
    {
        public string Name { get; set; } = string.Empty;
        public int TotalValue { get; set; }

        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        internal static WebRadarCorpse Create(LootCorpse corpse)
        {
            var pos = corpse.Position;
            return new WebRadarCorpse
            {
                Name = corpse.Name,
                TotalValue = corpse.TotalValue,
                WorldX = pos.X,
                WorldY = pos.Y,
                WorldZ = pos.Z,
            };
        }
    }
}
