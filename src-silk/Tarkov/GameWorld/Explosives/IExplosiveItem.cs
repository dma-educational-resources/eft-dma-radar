using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Explosives
{
    /// <summary>
    /// Base interface for all explosive entities (grenades, tripwires, mortar projectiles).
    /// Radar-map rendering only — no ESP in Silk.
    /// </summary>
    internal interface IExplosiveItem
    {
        /// <summary>Base address of the explosive item in game memory.</summary>
        ulong Addr { get; }

        /// <summary>True if the explosive is in an active state; false = ready for cleanup.</summary>
        bool IsActive { get; }

        /// <summary>World position of the explosive.</summary>
        ref Vector3 Position { get; }

        /// <summary>
        /// Refresh this item's state via direct DMA reads.
        /// Used for initial construction only; per-tick updates use scatter.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Queue scatter read entries for this explosive's per-tick state.
        /// Called once per tick before <see cref="ScatterReadMap.Execute"/>.
        /// </summary>
        void QueueScatterReads(ScatterReadIndex idx);

        /// <summary>
        /// Apply results from the completed scatter read.
        /// Called once per tick after <see cref="ScatterReadMap.Execute"/>.
        /// </summary>
        void ApplyScatterResults(ScatterReadIndex idx);

        /// <summary>
        /// Draw this explosive on the radar canvas.
        /// </summary>
        void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer);
    }
}
