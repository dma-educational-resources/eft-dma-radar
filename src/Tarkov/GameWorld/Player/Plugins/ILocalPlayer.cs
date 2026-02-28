namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    /// <summary>
    /// Interface defining a player that is the LocalPlayer running this software.
    /// </summary>
    public interface ILocalPlayer : IPlayer
    {
        private static ulong _accountId;
        /// <summary>
        /// LocalPlayer's Account Id.
        /// New entries will be posted to the Server/API.
        /// </summary>
        public static ulong AccountId
        {
            set
            {
                if (value != 0 && value < 0x3B9ACA00ul && value != _accountId)
                {
                    _accountId = value;
                }
            }
        }
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
