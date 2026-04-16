using System.Collections.Frozen;
using System.Numerics;

namespace eft_dma_radar.Silk.Misc.Data
{
    /// <summary>
    /// Static switch positions per map.
    /// First key: Map ID, Second key: Switch display name, Value: World position.
    /// </summary>
    internal static class SwitchData
    {
        public static readonly FrozenDictionary<string, FrozenDictionary<string, Vector3>> Switches =
            new Dictionary<string, FrozenDictionary<string, Vector3>>(StringComparer.OrdinalIgnoreCase)
            {
                ["bigmap"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["switch_develop_00000_Switch"] = new Vector3(113.554016f, -4.01100159f, -43.5665855f),
                    ["ZB-013 Power Switch"] = new Vector3(352.230316f, 2.61458874f, -40.8052826f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),

                ["Lighthouse"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Lightkeeper Switch 1"] = new Vector3(445.3035f, 33.391f, 457.5599f),
                    ["Lightkeeper Switch 2"] = new Vector3(444.6317f, 33.391f, 457.6145f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),

                ["RezervBase"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Bunker Hermetic Door Power Switch"] = new Vector3(-60.7597275f, -5.55217171f, 78.22821f),
                    ["D-2 Power Switch"] = new Vector3(-117.184174f, -12.954f, 22.6676826f),
                    ["D-2 Door Switch"] = new Vector3(-117.449867f, -16.9842987f, 168.546936f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),

                ["Interchange"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mall Main Power Switch"] = new Vector3(-201.108f, 23.1857f, -357.8291f),
                    ["Non-Kiba Alarms Switch"] = new Vector3(-46.555f, 37.347f, -55.202f),
                    ["Alarms Switch"] = new Vector3(-67.1342545f, 27.9506f, 53.7422676f),
                    ["Saferoom Exfil Switch"] = new Vector3(-50.6210022f, 22.632f, 45.617f),
                    ["Saferoom Exfil Unlock Switch"] = new Vector3(-51.547f, 36.86f, -125.440712f),
                    ["Object 14 Container Switch"] = new Vector3(-47.698f, 22.891f, 42.6198f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),

                ["laboratory"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Med Elevator Power Button"] = new Vector3(-124.758f, -2.31599617f, -313.806f),
                    ["Main Elevator Call Button"] = new Vector3(-281.022f, -2.83799934f, -335.477f),
                    ["Med Elevator Call Button"] = new Vector3(-114.112f, -2.84599972f, -343.2f),
                    ["Hangar Gate Switch"] = new Vector3(-170.18f, 5.185f, -281.508f),
                    ["Cargo Elevator Call Button"] = new Vector3(-114.037f, 5.31399727f, -406.427979f),
                    ["Cargo Elevator Extract Button"] = new Vector3(-112.378006f, 5.353998f, -406.806f),
                    ["Main Elevator Power Button"] = new Vector3(-271.439f, -2.380001f, -366.10498f),
                    ["Water Level Switch"] = new Vector3(-129.519989f, -6.7559967f, -244.764511f),
                    ["Main Elevator Extract Button"] = new Vector3(-282.361f, -2.91199875f, -335.86f),
                    ["Parking Gate Switch"] = new Vector3(-243.443f, 5.076f, -382.513f),
                    ["Sewage Conduit Pump Button"] = new Vector3(-136.76f, -2.82599926f, -254.510513f),
                    ["Cargo Elevator Power Button"] = new Vector3(-121.007996f, -2.83698225f, -353.548f),
                    ["Med Elevator Extract Button"] = new Vector3(-112.802f, -2.84599972f, -342.762f),
                    ["Alarm Switch"] = new Vector3(-220.756f, 5.249f, -381.263f),
                    ["Containment Block Power Switch"] = new Vector3(-112.411f, 1.06300008f, -435.429016f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),

                ["Labyrinth"] = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Alarm Switch 1"] = new Vector3(-4.279f, 1.51507258f, 55.87111f),
                    ["Alarm Switch 2"] = new Vector3(1.05600023f, 1.50388885f, 7.16910934f),
                    ["Alarm Switch 3"] = new Vector3(-13.3701f, 1.63888884f, 36.08191f),
                    ["Sealed Door 1"] = new Vector3(40.1832f, 0.298618853f, 19.1903f),
                    ["Alarm Switch 4"] = new Vector3(8.937f, 1.54792f, 28.68511f),
                    ["Fire Trap Switch"] = new Vector3(-43.587f, 1.56588888f, -10.9208889f),
                    ["Alarm Switch 5"] = new Vector3(-9.011f, 1.66788888f, 1.57610893f),
                    ["Toxic Pool Trap Switch"] = new Vector3(-31.7254715f, 2.08068f, 58.2143f),
                    ["Sealed Door 2"] = new Vector3(-49.34948f, 1.70373631f, -11.756073f),
                    ["Shotgun Trap Switch"] = new Vector3(25.4025669f, 1.37f, 59.453f),
                    ["Toxic Puddle Switch"] = new Vector3(46.42f, 1.031f, 11.084f),
                    ["Steam Trap Switch"] = new Vector3(2.659f, 2.109861f, -31.705f),
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the switches for a given map ID, or empty if none exist.
        /// </summary>
        public static IReadOnlyList<(string Name, Vector3 Position)> GetSwitchesForMap(string? mapId)
        {
            if (mapId is null || !Switches.TryGetValue(mapId, out var dict))
                return [];

            var result = new List<(string, Vector3)>(dict.Count);
            foreach (var kvp in dict)
                result.Add((kvp.Key, kvp.Value));
            return result;
        }
    }
}
