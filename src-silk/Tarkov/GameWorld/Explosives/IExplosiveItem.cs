using VmmSharpEx.Scatter;

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
        /// Queue scatter reads and register a Completed callback to process results.
        /// Called once per tick before <see cref="VmmScatter.Execute"/>.
        /// </summary>
        void OnRefresh(VmmScatter scatter);

        /// <summary>
        /// Draw this explosive on the radar canvas.
        /// </summary>
        void Draw(SKCanvas canvas, MapParams mapParams, MapConfig mapCfg, Player.Player localPlayer);
    }
}
