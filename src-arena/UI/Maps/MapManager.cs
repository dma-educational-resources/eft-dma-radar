using System.Collections.Frozen;

namespace eft_dma_radar.Arena.UI.Maps
{
    /// <summary>
    /// Manages map configs and the currently active <see cref="RadarMap"/>.
    /// Thread-safe lazy load — rasterizes SVG layers on a background thread to avoid
    /// blocking the render loop. The render thread checks <see cref="IsLoading"/> and
    /// shows a status message until the map is ready.
    /// </summary>
    internal static class MapManager
    {
        private static FrozenDictionary<string, MapConfig> _configs =
            FrozenDictionary<string, MapConfig>.Empty;

        private static RadarMap? _currentMap;
        private static string? _currentMapId;
        private static volatile bool _isLoading;
        private static readonly Lock _lock = new();

        /// <summary>Maps directory in the output tree (mirrors silk layout).</summary>
        private static string MapsDir =>
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "Maps");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Currently active map, or <see langword="null"/> if none loaded.</summary>
        internal static RadarMap? Map => _currentMap;

        /// <summary>Whether a map is currently being loaded on a background thread.</summary>
        internal static bool IsLoading => _isLoading;

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
        /// Returns <c>true</c> if the given map ID is a known/valid map in our config set.
        /// </summary>
        internal static bool IsKnownMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return false;

            return _configs.ContainsKey(mapId);
        }

        /// <summary>
        /// Kicks off a background load for the map matching <paramref name="mapId"/>.
        /// Returns immediately — the render thread should check <see cref="IsLoading"/>
        /// and <see cref="Map"/> each frame. No-ops if the requested map is already
        /// active or a load is in progress.
        /// </summary>
        internal static void LoadMap(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return;

            lock (_lock)
            {
                if (string.Equals(_currentMapId, mapId, StringComparison.OrdinalIgnoreCase))
                    return;

                if (_isLoading)
                    return;

                if (!_configs.TryGetValue(mapId, out var config))
                {
                    if (!_configs.TryGetValue("default", out config))
                    {
                        Log.WriteLine($"[MapManager] No config found for '{mapId}' and no default.");
                        return;
                    }
                    Log.WriteLine($"[MapManager] No config for '{mapId}', using default.");
                }

                var old = _currentMap;
                _currentMap = null;
                _currentMapId = null;
                old?.Dispose();

                _isLoading = true;
                var capturedConfig = config;
                var capturedId = mapId;

                Task.Run(() =>
                {
                    try
                    {
                        Log.WriteLine($"[MapManager] Loading map '{capturedId}' ({capturedConfig.Name})...");
                        var sw = Stopwatch.StartNew();
                        var map = new RadarMap(MapsDir, capturedId, capturedConfig);
                        sw.Stop();
                        Log.WriteLine($"[MapManager] Map '{capturedConfig.Name}' ready ({sw.ElapsedMilliseconds}ms).");

                        lock (_lock)
                        {
                            _currentMap = map;
                            _currentMapId = capturedId;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLine($"[MapManager] Failed to load map '{capturedId}': {ex}");
                    }
                    finally
                    {
                        _isLoading = false;
                    }
                });
            }
        }
    }
}
