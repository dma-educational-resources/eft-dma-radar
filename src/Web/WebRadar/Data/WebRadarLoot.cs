using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using MessagePack;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    [MessagePackObject]
    public sealed class WebRadarLoot
    {
        [Key(0)] public string ShortName { get; set; }
        [Key(1)] public int Price { get; set; }

        // Flattened position (JSON safe)
        [Key(2)] public float X { get; set; }
        [Key(3)] public float Y { get; set; }
        [Key(4)] public float Z { get; set; }

        [Key(5)] public bool IsMeds { get; set; }
        [Key(6)] public bool IsFood { get; set; }
        [Key(7)] public bool IsBackpack { get; set; }

        [Key(8)] public string BsgId { get; set; }

        public static WebRadarLoot CreateFromLoot(LootItem loot)
        {
            var p = loot.Position;
            var map = XMMapManager.Map;
            var mappos = loot.Position.ToMapPos(map.Config);
            return new WebRadarLoot
            {
                ShortName = loot.ShortName,
                Price = loot.Price,

                X = mappos.X,
                Y = mappos.Y,
                Z = p.Z,

                IsMeds = loot.IsMeds,
                IsFood = loot.IsFood,
                IsBackpack = loot.IsBackpack,
                BsgId = loot.ID
            };
        }
    }
}
