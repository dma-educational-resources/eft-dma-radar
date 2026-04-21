namespace eft_dma_radar.Silk.Web.Data
{
    /// <summary>
    /// Killfeed entry sent to the web radar client.
    /// </summary>
    public sealed class WebRadarKillfeedEntry
    {
        public string Killer { get; set; } = "";
        public string Victim { get; set; } = "";
        public string Weapon { get; set; } = "";
        public int VictimLevel { get; set; }
        public string KillerSide { get; set; } = "";

        /// <summary>UTC timestamp string (ISO 8601) when the kill was logged.</summary>
        public string Timestamp { get; set; } = "";

        internal static WebRadarKillfeedEntry Create(Tarkov.GameWorld.Loot.KillfeedEntry e) => new()
        {
            Killer      = e.Killer,
            Victim      = e.Victim,
            Weapon      = e.Weapon,
            VictimLevel = e.VictimLevel,
            KillerSide  = e.KillerSide.ToString(),
            Timestamp   = e.Timestamp.ToString("O"),
        };
    }
}
