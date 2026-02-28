using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.UI.Misc;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    public sealed class WebRadarExfil
    {
        public string Name { get; set; }

        // World position
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        // Status snapshot
        public WebRadarExfilStatus Status { get; set; }

        // FINAL server-side decision
        public bool IsAvailableForPlayer { get; set; }

        public bool IsSecret { get; set; }
        public static WebRadarExfil CreateFromExfil(Exfil exfil)
        {
            var p = exfil.Position;
            var map = XMMapManager.Map;
            var mappos = exfil.Position.ToMapPos(map.Config);
            return new WebRadarExfil
            {
                Name = exfil.Name,
                X = mappos.X,
                Y = mappos.Y,
                Z = p.Z,
                Status = (WebRadarExfilStatus)exfil.Status,
                IsAvailableForPlayer = exfil.IsAvailableForPlayer(Memory.LocalPlayer),
                IsSecret = exfil.IsSecret
            };
        } 
    }

    public enum WebRadarExfilStatus
    {
        Open,
        Pending,
        Closed
    }   
}
