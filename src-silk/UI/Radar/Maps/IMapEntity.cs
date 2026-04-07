namespace eft_dma_radar.Silk.UI.Radar.Maps
{
    /// <summary>
    /// An entity that can draw itself on the radar map.
    /// Mirrors WPF IMapEntity pattern.
    /// </summary>
    internal interface IMapEntity
    {
        void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapConfig);
    }
}
