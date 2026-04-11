namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Map geometry snapshot for the web radar client.
    /// </summary>
    public sealed class WebRadarMapInfo
    {
        public string Name { get; set; } = string.Empty;
        public string MapId { get; set; } = string.Empty;

        public float OriginX { get; set; }
        public float OriginY { get; set; }
        public float Scale { get; set; }
        public float SvgScale { get; set; }

        public List<WebRadarMapLayer>? Layers { get; set; }
    }

    /// <summary>
    /// A single height-constrained map layer.
    /// </summary>
    public sealed class WebRadarMapLayer
    {
        public float? MinHeight { get; set; }
        public float? MaxHeight { get; set; }
        public bool DimBaseLayer { get; set; }
        public string Filename { get; set; } = string.Empty;
    }
}
