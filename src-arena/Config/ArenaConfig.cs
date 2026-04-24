using System.IO;

namespace eft_dma_radar.Arena.Config
{
    public sealed class ArenaConfig
    {
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-arena");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        // ── DMA ──────────────────────────────────────────────────────────────

        [JsonPropertyName("device")]
        public string DeviceStr { get; set; } = "fpga";

        [JsonPropertyName("memmapEnabled")]
        public bool MemMapEnabled { get; set; } = false;

        // ── Logging ───────────────────────────────────────────────────────────

        [JsonPropertyName("debugLogging")]
        public bool DebugLogging { get; set; } = false;

        // ── Window ────────────────────────────────────────────────────────────

        [JsonPropertyName("windowWidth")]
        public int WindowWidth { get; set; } = 1280;

        [JsonPropertyName("windowHeight")]
        public int WindowHeight { get; set; } = 1024;

        [JsonPropertyName("windowMaximized")]
        public bool WindowMaximized { get; set; } = false;

        [JsonPropertyName("targetFps")]
        public int TargetFps { get; set; } = 144;

        [JsonPropertyName("uiScale")]
        public float UIScale { get; set; } = 1.0f;

        // ── Radar UI ──────────────────────────────────────────────────────────

        [JsonPropertyName("zoom")]
        public int Zoom { get; set; } = 100;

        [JsonPropertyName("freeMode")]
        public bool FreeMode { get; set; } = false;

        [JsonPropertyName("showAimlines")]
        public bool ShowAimlines { get; set; } = true;

        [JsonPropertyName("showNames")]
        public bool ShowNames { get; set; } = true;

        [JsonPropertyName("showGrid")]
        public bool ShowGrid { get; set; } = true;

        // ── Game / Camera ─────────────────────────────────────────────────────

        /// <summary>Width of the game's render resolution (used by CameraManager W2S).</summary>
        [JsonPropertyName("gameMonitorWidth")]
        public int GameMonitorWidth { get; set; } = 1920;

        /// <summary>Height of the game's render resolution (used by CameraManager W2S).</summary>
        [JsonPropertyName("gameMonitorHeight")]
        public int GameMonitorHeight { get; set; } = 1080;

        // ── Persistence ───────────────────────────────────────────────────────

        public static ArenaConfig Load()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);

                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<ArenaConfig>(json);
                    if (cfg is not null)
                    {
                        Log.WriteLine($"[ArenaConfig] Loaded from {ConfigPath}");
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ArenaConfig] Load failed: {ex.Message} — using defaults.");
            }

            var defaults = new ArenaConfig();
            defaults.Save();
            return defaults;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ArenaConfig] Save failed: {ex.Message}");
            }
        }
    }
}
