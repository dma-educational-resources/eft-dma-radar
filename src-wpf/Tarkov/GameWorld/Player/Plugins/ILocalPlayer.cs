namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    /// <summary>
    /// Interface defining a player that is the LocalPlayer running this software.
    /// </summary>
    public interface ILocalPlayer : IPlayer
    {
        /// <summary>
        /// Current Player State.
        /// </summary>
        public static ulong PlayerState = 0;
        /// <summary>
        /// Current HandsController Instance.
        /// </summary>
        public static ulong HandsController = 0;
    }
}
