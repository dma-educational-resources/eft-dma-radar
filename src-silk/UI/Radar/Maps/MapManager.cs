using System.Collections.Frozen;

namespace eft_dma_radar.Silk.UI.Radar.Maps
{
    /// <summary>
    /// Manages map configs and the currently active <see cref="RadarMap"/>.
    /// Thread-safe lazy load — rasterizes SVG layers on first use per map ID.
    /// Self-contained map manager with no external dependencies.
    /// </summary>
    internal static class MapManager
    {
        private static FrozenDictionary<string, MapConfig> _configs =
            FrozenDictionary<string, MapConfig>.Empty;

        private static RadarMap? _currentMap;
        private static string? _currentMapId;
        private static readonly Lock _lock = new();

        /// <summary>Maps directory in the output tree.</summary>
        private static string MapsDir =>
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "Maps");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Currently active map, or <see langword="null"/> if none loaded.</summary>
        internal static RadarMap? Map => _currentMap;

        /// <summary>
        /// Scans the Maps directory, deserializes all JSON configs, and caches them.
        /// Call once at startup from <c>Program.cs</c>.
        /// </summary>
        internal static void ModuleInit()
        {
            var dir = MapsDir;
            if (!Directory.Exists(dir))
            {
                Log.WriteLine($"[MapManager] Maps directory not found: {dir}");
                return;
            }

            var builder = new Dictionary<string, MapConfig>(StringComparer.OrdinalIgnoreCase);
            int loaded = 0, skipped = 0;

            foreach (var jsonFile in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(jsonFile);
                    var config = JsonSerializer.Deserialize<MapConfig>(stream, _jsonOpts);
                    if (config is null || config.MapLayers.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    foreach (var id in config.MapID)
                    {
                        if (!string.IsNullOrWhiteSpace(id))
                            builder[id] = config;
                    }

                    loaded++;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[MapManager] Failed to load '{Path.GetFileName(jsonFile)}': {ex.Message}");
                    skipped++;
                }
            }

            _configs = builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            Log.WriteLine($"[MapManager] Loaded {loaded} map configs ({_configs.Count} IDs), skipped {skipped}.");
        }

        /// <summary>
        /// Loads (or reloads) the map matching <paramref name="mapId"/>.
        /// Falls back to the "default" config if the ID is not found.
        /// No-ops if the requested map is already active.
        /// </summary>
        internal static void LoadMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return;

            lock (_lock)
            {
                // Already loaded?
                if (string.Equals(_currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
                    return;

                // Resolve config
                if (!_configs.TryGetValue(mapId, out var config))
                {
                    // Fallback to default
                    if (!_configs.TryGetValue("default", out config))
                    {
                        Log.WriteLine($"[MapManager] No config found for '{mapId}' and no default.");
                        return;
                    }
                    Log.WriteLine($"[MapManager] No config for '{mapId}', using default.");
                }

                // Dispose old map
                var old = _currentMap;
                _currentMap = null;
                _currentMapId = null;
                old?.Dispose();

                try
                {
                    Log.WriteLine($"[MapManager] Loading map '{mapId}' ({config.Name})...");
                    var map = new RadarMap(MapsDir, mapId, config);
                    _currentMap   = map;
                    _currentMapId = mapId;
                    Log.WriteLine($"[MapManager] Map '{config.Name}' ready.");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[MapManager] Failed to load map '{mapId}': {ex}");
                }
            }
        }
    }
}
