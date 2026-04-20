using eft_dma_radar.Silk.Tarkov.GameWorld.Exits;

namespace eft_dma_radar.Silk.Web.Data
{
    /// <summary>
    /// Flattened exfil snapshot for the web radar client.
    /// </summary>
    public sealed class WebRadarExfil
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>0 = Closed, 1 = Pending, 2 = Open.</summary>
        public int Status { get; set; }

        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public float WorldZ { get; set; }

        internal static WebRadarExfil Create(Exfil exfil)
        {
            var pos = exfil.Position;
            return new WebRadarExfil
            {
                Name = exfil.Name,
                Status = (int)exfil.Status,
                WorldX = pos.X,
                WorldY = pos.Y,
                WorldZ = pos.Z,
            };
        }
    }
}
