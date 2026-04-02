

using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.Tarkov.Unity;

namespace eft_dma_radar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// Defines a contract for a point that can be used to exit the map.
    /// </summary>
    public interface IExitPoint : IWorldEntity, IMapEntity, IMouseoverEntity, IESPEntity
    {
    }
}
