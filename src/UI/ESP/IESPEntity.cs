using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Common.Unity;

namespace eft_dma_radar.UI.ESP
{
    /// <summary>
    /// Defines an entity that can be drawn on Fuser ESP.
    /// </summary>
    public interface IESPEntity : IWorldEntity
    {
        /// <summary>
        /// Draw this Entity on Fuser ESP.
        /// </summary>
        /// <param name="canvas">SKCanvas instance to draw on.</param>
        void DrawESP(SKCanvas canvas, LocalPlayer localPlayer);
    }
}
