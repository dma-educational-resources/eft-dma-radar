using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.UI.Misc;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    public sealed class WebRadarTransit
    {
        public string Name { get; set; }

        // World position
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public static WebRadarTransit CreateFromTransit(TransitPoint Transit)
        {
            var p = Transit.Position;
            var map = XMMapManager.Map;
            var mappos = Transit.Position.ToMapPos(map.Config);
            return new WebRadarTransit
            {
                Name = Transit.Name,
                X = mappos.X,
                Y = mappos.Y,
                Z = p.Z
            };
        } 
    } 
}
