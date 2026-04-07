namespace eft_dma_radar.Silk.Tarkov.GameWorld.Player
{
    /// <summary>
    /// The local player (MainPlayer). Overrides <see cref="IsLocalPlayer"/> to <c>true</c>.
    /// </summary>
    public sealed class LocalPlayer : Player
    {
        public override bool IsLocalPlayer => true;

        protected override (SKPaint dot, SKPaint text) GetPaints()
        {
            return (SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);
        }
    }
}
