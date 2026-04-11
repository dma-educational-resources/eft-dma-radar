namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Converts a <see cref="MapConfig"/> to a <see cref="WebRadarMapInfo"/> for the web client.
    /// </summary>
    internal static class WebRadarMapConverter
    {
        public static WebRadarMapInfo? Convert(MapConfig? cfg)
        {
            if (cfg is null)
                return null;

            return new WebRadarMapInfo
            {
                Name = cfg.Name,
                MapId = cfg.MapID.FirstOrDefault() ?? string.Empty,
                OriginX = cfg.X,
                OriginY = cfg.Y,
                Scale = cfg.Scale,
                SvgScale = cfg.SvgScale,
                Layers = cfg.MapLayers.Select(static l => new WebRadarMapLayer
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
