namespace eft_dma_radar.Silk.Web.Data
{
    /// <summary>
    /// Player type classification for the web radar client.
    /// </summary>
    public enum WebPlayerType : int
    {
        Bot = 0,
        LocalPlayer = 1,
        Teammate = 2,
        Player = 3,
        PlayerScav = 4,
        Raider = 5,
        Boss = 6
    }
}
