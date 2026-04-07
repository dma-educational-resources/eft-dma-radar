namespace eft_dma_radar.Silk.UI.Radar.Maps
{
    /// <summary>
    /// Interface for a radar map that can draw itself and provide coordinate parameters.
    /// Radar map interface.
    /// </summary>
    internal interface IRadarMap : IDisposable
    {
        string ID { get; }
        MapConfig Config { get; }
        void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds);
        MapParams GetParameters(SKSize canvasSize, int zoom, ref Vector2 centerMapPos);
    }
}
