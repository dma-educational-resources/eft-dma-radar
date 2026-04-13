using System.Collections.Frozen;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// Static map ID → friendly display name mapping.
    /// Used by transit points to show "Transit to Customs" instead of "Transit to bigmap".
    /// </summary>
    internal static class MapNames
    {
        public static readonly FrozenDictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = "Default",
                ["Labyrinth"] = "The Labyrinth",
                ["Terminal"] = "Terminal",
                ["woods"] = "Woods",
                ["shoreline"] = "Shoreline",
                ["rezervbase"] = "Reserve",
                ["laboratory"] = "Labs",
                ["interchange"] = "Interchange",
                ["factory4_day"] = "Factory",
                ["factory4_night"] = "Factory",
                ["bigmap"] = "Customs",
                ["lighthouse"] = "Lighthouse",
                ["tarkovstreets"] = "Streets",
                ["Sandbox"] = "Ground Zero",
                ["Sandbox_high"] = "Ground Zero",
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
