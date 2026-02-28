namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    public sealed class WebRadarUpdate
    {
        public uint Version { get; set; }
        public bool InGame { get; set; }
        public bool InRaid { get; set; }
        public string MapID { get; set; }

        public IEnumerable<WebRadarPlayer> Players { get; set; }
        public IEnumerable<WebRadarLoot> Loot { get; set; }
        public IEnumerable<WebRadarDoor> Doors { get; set; }
        public IEnumerable<WebRadarExfil> Exfils { get; set; }
        public IEnumerable<WebRadarTransit> Transits { get; set; }

        public WebRadarMapInfo Map { get; set; }

        public DateTime SendTime { get; set; }
    }
}
