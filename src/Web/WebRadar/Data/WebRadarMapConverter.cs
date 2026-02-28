using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.WebRadar.Data;
using System.Linq;

namespace eft_dma_radar.Tarkov.WebRadar
{
    internal static class WebRadarMapConverter
    {
        public static WebRadarMapInfo Convert(XMMapConfig cfg)
        {
            if (cfg == null) return null;

            return new WebRadarMapInfo
            {
                Name = cfg.Name,
                MapId = cfg.MapID.FirstOrDefault(),

                OriginX = cfg.X,
                OriginY = cfg.Y,
                Scale   = cfg.Scale,
                SvgScale = cfg.SvgScale,

                DisableDimming = false,

                Layers = cfg.MapLayers.Select(l => new WebRadarMapLayer
                {
                    MinHeight = l.MinHeight,
                    MaxHeight = l.MaxHeight,
                    DimBaseLayer = l.DimBaseLayer,
                    Filename = l.Filename
                }).ToList()
            };
        }
    }
}
