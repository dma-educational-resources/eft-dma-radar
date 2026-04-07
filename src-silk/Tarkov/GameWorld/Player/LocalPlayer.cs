namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// The local player (MainPlayer). Overrides <see cref="IsLocalPlayer"/> to <c>true</c>.
    /// Mirrors WPF LocalPlayer hierarchy.
    /// </summary>
    public sealed class LocalPlayer : Player
    {
        public override bool IsLocalPlayer => true;

        protected override (SKPaint dot, SKPaint text) GetPaints()
        {
            return (eft_dma_radar.UI.Misc.SKPaints.PaintLocalPlayer,
                    eft_dma_radar.UI.Misc.SKPaints.TextLocalPlayer);
        }
    }
}
