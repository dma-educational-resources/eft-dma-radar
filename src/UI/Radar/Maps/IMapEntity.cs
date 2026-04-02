using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Unity;
using SkiaSharp;

namespace eft_dma_radar.UI.Radar.Maps
{
    /// <summary>
    /// Defines an entity that can be drawn on the 2D Radar Map.
    /// </summary>
    public interface IMapEntity : IWorldEntity
    {
        /// <summary>
        /// Draw this Entity on the Radar Map.
        /// </summary>
        /// <param name="canvas">SKCanvas instance to draw on.</param>
        void Draw(SKCanvas canvas, XMMapParams mapParams, ILocalPlayer localPlayer);
    }
}
