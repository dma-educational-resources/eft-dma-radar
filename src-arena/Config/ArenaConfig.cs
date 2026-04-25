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

        [JsonPropertyName("showTeamTag")]
        public bool ShowTeamTag { get; set; } = false;

        [JsonPropertyName("showHeightDiff")]
        public bool ShowHeightDiff { get; set; } = true;

        [JsonPropertyName("showGrid")]
        public bool ShowGrid { get; set; } = true;

        // ── Aimview ───────────────────────────────────────────────────────────

        [JsonPropertyName("aimviewEnabled")]
        public bool AimviewEnabled { get; set; } = false;

        /// <summary>If true, use the live game ViewMatrix via CameraManager.WorldToScreen.</summary>
        [JsonPropertyName("aimviewUseAdvanced")]
        public bool AimviewUseAdvanced { get; set; } = true;

        /// <summary>Hide AI players in the Aimview widget.</summary>
        [JsonPropertyName("aimviewHideAI")]
        public bool AimviewHideAI { get; set; } = false;

        /// <summary>Show name + distance labels under each player dot.</summary>
        [JsonPropertyName("aimviewShowLabels")]
        public bool AimviewShowLabels { get; set; } = true;

        /// <summary>Draw skeleton bone segments on top of player dots when available (advanced mode only).</summary>
        [JsonPropertyName("aimviewDrawSkeletons")]
        public bool AimviewDrawSkeletons { get; set; } = true;

        /// <summary>Maximum render distance (meters).</summary>
        [JsonPropertyName("aimviewMaxDistance")]
        public float AimviewMaxDistance { get; set; } = 300f;

        /// <summary>Synthetic-mode zoom factor (only used when advanced mode is off / unavailable).</summary>
        [JsonPropertyName("aimviewZoom")]
        public float AimviewZoom { get; set; } = 1.0f;

        /// <summary>Eye height offset above the local player root (meters).</summary>
        [JsonPropertyName("aimviewEyeHeight")]
        public float AimviewEyeHeight { get; set; } = 1.5f;

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
