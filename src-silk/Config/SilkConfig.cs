using System.IO;

namespace eft_dma_radar.Silk.Config
{
    /// <summary>
    /// Minimal configuration for the Silk.NET radar.
    /// Loaded from / saved to a JSON file in %AppData%\eft-dma-radar-silk\.
    /// </summary>
    public sealed class SilkConfig
    {
        private static readonly string _configDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-silk");

        private static readonly string _configPath =
            Path.Combine(_configDir, "config.json");

        private static readonly JsonSerializerOptions _jsonWriteOptions = new() { WriteIndented = true };

        // ── DMA ─────────────────────────────────────────────────────────────────

        /// <summary>FPGA device string passed to MemProcFS (e.g. "fpga", "usb3380").</summary>
        public string DeviceStr { get; set; } = "fpga";

        /// <summary>Use a persisted memory map file for faster DMA init.</summary>
        public bool MemMapEnabled { get; set; } = true;

        // ── UI ──────────────────────────────────────────────────────────────────

        /// <summary>UI scaling factor (1.0 = 100%).</summary>
        public float UIScale { get; set; } = 1.0f;

        /// <summary>Target frames per second for the radar window.</summary>
        public int TargetFps { get; set; } = 60;

        /// <summary>Radar window width in pixels.</summary>
        public int WindowWidth { get; set; } = 1600;

        /// <summary>Radar window height in pixels.</summary>
        public int WindowHeight { get; set; } = 900;

        /// <summary>Whether the radar window starts maximized.</summary>
        public bool WindowMaximized { get; set; } = false;

        /// <summary>Hide loot and other clutter; show only players.</summary>
        public bool BattleMode { get; set; } = false;

        /// <summary>Draw players above all other entities.</summary>
        public bool PlayersOnTop { get; set; } = false;

        /// <summary>Draw lines connecting squad members.</summary>
        public bool ConnectGroups { get; set; } = true;

        /// <summary>Show aimlines extending from player markers indicating facing direction.</summary>
        public bool ShowAimlines { get; set; } = true;

        /// <summary>Aimline length in pixels for human players (PMC/PScav).</summary>
        public int AimlineLength { get; set; } = 15;

        /// <summary>Extend aimline when an enemy is facing the local player (High Alert).</summary>
        public bool HighAlert { get; set; } = true;

        // ── Exfils ──────────────────────────────────────────────────────────────

        /// <summary>Master toggle for exfil rendering on the radar.</summary>
        public bool ShowExfils { get; set; } = true;

        /// <summary>Hide exfils that are closed or not available to the local player.</summary>
        public bool HideInactiveExfils { get; set; } = true;

        // ── Loot ────────────────────────────────────────────────────────────────

        /// <summary>Master toggle for loot rendering on the radar.</summary>
        public bool ShowLoot { get; set; } = true;

        /// <summary>Minimum price (roubles) below which loot is hidden from the radar.</summary>
        public int LootMinPrice { get; set; } = 50_000;

        /// <summary>Price threshold (roubles) above which loot is highlighted as important.</summary>
        public int LootImportantPrice { get; set; } = 200_000;

        /// <summary>Show prices as price-per-slot instead of total price.</summary>
        public bool LootPricePerSlot { get; set; } = false;

        /// <summary>Price source for loot values (0 = Best, 1 = Flea, 2 = Trader).</summary>
        public int LootPriceSource { get; set; } = 0;

        // ── Profiles ────────────────────────────────────────────────────────────

        /// <summary>Enable tarkov.dev profile lookups for human players (KD, hours, etc.).</summary>
        public bool ProfileLookups { get; set; } = true;

        // ── Memory Writes ───────────────────────────────────────────────────────

        /// <summary>Master toggle for all memory write features.</summary>
        public bool MemWritesEnabled { get; set; } = false;

        // ── Persistence ─────────────────────────────────────────────────────────

        /// <summary>
        /// Load config from disk. Returns a default instance if the file does not exist or is corrupt.
        /// </summary>
        public static SilkConfig Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var cfg = JsonSerializer.Deserialize<SilkConfig>(json);
                    if (cfg is not null)
                    {
                        Log.WriteLine("[SilkConfig] Config loaded OK.");
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[SilkConfig] Failed to load config, using defaults: {ex.Message}");
            }

            Log.WriteLine("[SilkConfig] No config found, using defaults.");
            return new SilkConfig();
        }

        /// <summary>
        /// Save config to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                var json = JsonSerializer.Serialize(this, _jsonWriteOptions);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[SilkConfig] Failed to save config: {ex.Message}");
            }
        }
    }
}
