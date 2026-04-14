namespace eft_dma_radar.Silk.Web.WebRadar.Data
{
    /// <summary>
    /// Top-level radar state snapshot sent to the web client via <c>/api/radar</c>.
    /// </summary>
    public sealed class WebRadarUpdate
    {
        public uint Version { get; set; }
        public bool InGame { get; set; }
        public bool InRaid { get; set; }
        public string? MapID { get; set; }
        public DateTime SendTime { get; set; }

        public WebRadarMapInfo? Map { get; set; }
        public WebRadarPlayer[]? Players { get; set; }
        public WebRadarLootItem[]? Loot { get; set; }
        public WebRadarCorpse[]? Corpses { get; set; }
        public WebRadarContainer[]? Containers { get; set; }
        public WebRadarExfil[]? Exfils { get; set; }
    }
}
