using eft_dma_radar.Common.Misc;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace eft_dma_radar.Common.Maps
{
    /// <summary>
    /// Maintains Map Resources for this application.
    /// </summary>
    public static class XMMapManager
    {
        private static readonly Lock _sync = new();
        private static FrozenDictionary<string, XMMapConfig> _maps;
        private static string _mapsDirectory;

        /// <summary>
        /// Currently Loaded Map.
        /// </summary>
        public static IXMMap Map { get; private set; }

        /// <summary>
        /// Initialize this Module.
        /// ONLY CALL ONCE!
        /// </summary>
        public static void ModuleInit()
        {
            try
            {
                _mapsDirectory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "Maps");

                if (!Directory.Exists(_mapsDirectory))
                    throw new DirectoryNotFoundException($"Maps directory not found: {_mapsDirectory}");

                var mapsBuilder = new Dictionary<string, XMMapConfig>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(_mapsDirectory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    using var stream = File.OpenRead(file);
                    var config = JsonSerializer.Deserialize<XMMapConfig>(stream);

                    if (config == null || config.MapID == null)
                        continue;

                    foreach (var id in config.MapID)
                        mapsBuilder[id] = config;
                }

                if (mapsBuilder.Count == 0)
                    throw new Exception("No map configs found in Maps directory.");

                _maps = mapsBuilder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Failed to Initialize Maps!", ex);
            }
        }

        /// <summary>
        /// Update the current map and load resources into Memory.
        /// </summary>
        /// <param name="mapId">Id of map to load.</param>
        public static void LoadMap(string mapId)
        {
            lock (_sync)
            {
                try
                {
                    if (!_maps.TryGetValue(mapId, out var config))
                        config = _maps["default"];

                    Map?.Dispose();
                    Map = null;

                    Map = new XMSvgMap(_mapsDirectory, mapId, config);
                }
                catch (Exception ex)
                {
                    throw new Exception($"ERROR loading map '{mapId}'", ex);
                }
            }
        }
    }
}
