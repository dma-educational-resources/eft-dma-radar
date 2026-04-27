using System.Collections.Frozen;

namespace eft_dma_radar.Arena.GameWorld.Exits
{
    /// <summary>
    /// Static map ID → friendly display name mapping for Arena maps.
    /// Scene names confirmed via live MapID reads.
    /// </summary>
    internal static class MapNames
    {
        public static readonly FrozenDictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Official Arena scenes (IDs sourced from the pre-1.0 arena radar map configs).
                ["Arena_AirPit"]            = "Air Pit",
                ["Arena_Bay5"]              = "Bay 5",
                ["Arena_Yard"]              = "Block (Yard)",
                ["Arena_Bowl"]              = "Bowl",
                ["Arena_AutoService"]       = "Chop Shop",
                ["Arena_equator_TDM_02"]    = "Equator",
                ["Arena_Prison"]            = "Fort",
                ["Arena_Iceberg"]           = "Iceberg",
                ["Arena_saw"]               = "Sawmill",
                ["Arena_RailwayStation"]    = "Skybridge",
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a friendly display name for the given map ID, or the raw ID if unknown.
        /// </summary>
        public static string GetDisplayName(string mapId) =>
            Names.TryGetValue(mapId, out var name) ? name : mapId;
    }
}
