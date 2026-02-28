namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    public sealed class WebRadarMapInfo
    {
        public string Name { get; set; }
        public string MapId { get; set; }

        public float OriginX { get; set; }
        public float OriginY { get; set; }

        public float Scale { get; set; }
        public float SvgScale { get; set; }

        public bool DisableDimming { get; set; }

        public List<WebRadarMapLayer> Layers { get; set; }
    }

    public sealed class WebRadarMapLayer
    {
        public float? MinHeight { get; set; }
        public float? MaxHeight { get; set; }
        public bool DimBaseLayer { get; set; }
        public string Filename { get; set; }
    }
}
