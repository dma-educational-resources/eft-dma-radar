using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.UI.Misc;
using MessagePack;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    public sealed class WebRadarDoor
    {
        public EDoorState DoorState { get; init; }
        public string Id { get; init; }
        public string? KeyId { get; init; }

        //ned position (JSON safe)
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }

        public static WebRadarDoor CreateFromDoor(Door door)
        {
            var p = door.Position;
            var map = XMMapManager.Map;
            var mappos = door.Position.ToMapPos(map.Config);
            return new WebRadarDoor
            {
                DoorState = door.DoorState,
                Id = door.Id,
                KeyId = door.KeyId,
                X = mappos.X,
                Y = mappos.Y,
                Z = p.Z
            };
        }
    }
}
