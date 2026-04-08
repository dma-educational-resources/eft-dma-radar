#pragma warning disable IDE0130
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using MessagePack;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    [MessagePackObject]
    public sealed class WebRadarContainer
    {
        [Key(0)] public string Name { get; set; }
        [Key(1)] public string ContainerId { get; set; }
        [Key(2)] public bool Searched { get; set; }

        // Map-space position
        [Key(3)] public float X { get; set; }
        [Key(4)] public float Y { get; set; }

        // World-space position (for aimview / distance calculations)
        [Key(5)] public float WorldX { get; set; }
        [Key(6)] public float WorldY { get; set; }
        [Key(7)] public float WorldZ { get; set; }

        public static WebRadarContainer CreateFromContainer(StaticLootContainer container)
        {
            var p = container.Position;
            var map = XMMapManager.Map;
            var mapPos = container.Position.ToMapPos(map.Config);
            return new WebRadarContainer
            {
                Name = container.Name,
                ContainerId = container.ID,
                Searched = container.Searched,

                X = mapPos.X,
                Y = mapPos.Y,

                WorldX = p.X,
                WorldY = p.Y,
                WorldZ = p.Z
            };
        }
    }
}
