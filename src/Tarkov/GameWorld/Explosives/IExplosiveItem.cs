using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Base interface for all explosive entities (grenades, tripwires, mortars, etc).
    /// </summary>
    public interface IExplosiveItem : IWorldEntity, IMapEntity, IESPEntity
    {
        /// <summary>
        /// Base address of the explosive item.
        /// </summary>
        ulong Addr { get; }

        /// <summary>
        /// True if the explosive is in an active state, otherwise False.
        /// Used for cleanup.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Slow path refresh (direct DMA reads).
        /// Still used in constructors / fallback.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Queue ScatterRead entries for this item (fast path).
        /// Called from ExplosivesManager.Refresh() before map.Execute().
        /// </summary>
        void QueueScatterReads(ScatterReadIndex idx);

        /// <summary>
        /// Apply ScatterRead results for this item.
        /// Called from ExplosivesManager.Refresh() after map.Execute().
        /// </summary>
        void OnRefresh(ScatterReadIndex idx);
    }
}
