using System.IO;

namespace eft_dma_radar.Silk.Config
{
    /// <summary>
    /// Mode for how a hotkey triggers its action.
    /// </summary>
    public enum HotkeyMode
    {
        Toggle = 0,
        OnKey = 1
    }

    /// <summary>
    /// Individual hotkey entry for each action.
    /// </summary>
    public sealed class HotkeyEntry
    {
        /// <summary>If the hotkey is enabled.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Hotkey trigger mode: Toggle or OnKey (Hold).</summary>
        [JsonPropertyName("mode")]
        public HotkeyMode Mode { get; set; } = HotkeyMode.Toggle;

        /// <summary>Virtual keycode (int) for the hotkey. -1 = unset.</summary>
        [JsonPropertyName("key")]
        public int Key { get; set; } = -1;
    }

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

        // Debounced save: dirty flag + timestamp
        [JsonIgnore]
        private volatile bool _dirty;
        [JsonIgnore]
        private long _dirtyTimestamp;
        private const long DebounceSaveMs = 500;

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

        /// <summary>Show the aimview widget (first-person projection of nearby players).</summary>
        public bool ShowAimview { get; set; } = true;

        /// <summary>Show filtered loot items in the aimview widget.</summary>
        public bool AimviewShowLoot { get; set; } = true;

        /// <summary>Show nearby corpses with gear value in the aimview widget.</summary>
        public bool AimviewShowCorpses { get; set; } = true;

        /// <summary>Show nearby static containers in the aimview widget.</summary>
        public bool AimviewShowContainers { get; set; } = true;

        /// <summary>Max distance (meters) for players to appear in the aimview.</summary>
        public float AimviewPlayerDistance { get; set; } = 300f;

        /// <summary>Max distance (meters) for loot/corpses to appear in the aimview.</summary>
        public float AimviewLootDistance { get; set; } = 15f;

        /// <summary>Eye height offset (meters) above body root for the aimview camera.</summary>
        public float AimviewEyeHeight { get; set; } = 1.35f;

        /// <summary>Zoom level for the aimview (1.0 = ~90° FOV, higher = narrower/zoomed in).</summary>
        public float AimviewZoom { get; set; } = 1.0f;

        /// <summary>Aimline length in pixels for human players (PMC/PScav).</summary>
        public int AimlineLength { get; set; } = 15;

        /// <summary>Extend aimline when an enemy is facing the local player (High Alert).</summary>
        public bool HighAlert { get; set; } = true;

        // ── Widget Visibility ───────────────────────────────────────────────────

        /// <summary>Whether the Players widget is open.</summary>
        public bool ShowPlayersWidget { get; set; } = true;

        /// <summary>Whether the Loot widget is open.</summary>
        public bool ShowLootWidget { get; set; } = false;

        /// <summary>Whether the Aimview widget is open.</summary>
        public bool ShowAimviewWidget { get; set; } = true;

        /// <summary>Whether the unified settings overlay is open.</summary>
        public bool ShowSettingsOverlay { get; set; } = false;

        /// <summary>Whether the Loot Filters panel is open.</summary>
        public bool ShowLootFiltersPanel { get; set; } = false;

        /// <summary>Whether the Hotkey Manager panel is open.</summary>
        public bool ShowHotkeyPanel { get; set; } = false;

        // ── Exfils ──────────────────────────────────────────────────────────────

        /// <summary>Master toggle for exfil rendering on the radar.</summary>
        public bool ShowExfils { get; set; } = true;

        /// <summary>Hide exfils that are closed or not available to the local player.</summary>
        public bool HideInactiveExfils { get; set; } = true;

        // ── Transits ────────────────────────────────────────────────────────────

        /// <summary>Master toggle for transit point rendering on the radar.</summary>
        public bool ShowTransits { get; set; } = true;

        // ── Doors ───────────────────────────────────────────────────────────────

        /// <summary>Master toggle for keyed door rendering on the radar.</summary>
        public bool ShowDoors { get; set; } = true;

        /// <summary>Show locked doors on the radar.</summary>
        public bool ShowLockedDoors { get; set; } = true;

        /// <summary>Show unlocked (open/shut) doors on the radar.</summary>
        public bool ShowUnlockedDoors { get; set; } = true;

        /// <summary>Only show doors that are near important (high-value) loot items.</summary>
        public bool DoorsOnlyNearLoot { get; set; } = true;

        /// <summary>Maximum distance (meters) from a door to important loot for it to be shown.</summary>
        public float DoorLootProximity { get; set; } = 25f;

        // ── Loot ────────────────────────────────────────────────────────────────

        /// <summary>Master toggle for loot rendering on the radar.</summary>
        public bool ShowLoot { get; set; } = true;

        /// <summary>Show corpse X markers on the radar (when loot is enabled).</summary>
        public bool ShowCorpses { get; set; } = true;

        /// <summary>Show static loot containers on the radar (when loot is enabled).</summary>
        public bool ShowContainers { get; set; } = true;

        /// <summary>Show container name labels next to container markers.</summary>
        public bool ShowContainerNames { get; set; } = true;

        /// <summary>Minimum price (roubles) below which loot is hidden from the radar.</summary>
        public int LootMinPrice { get; set; } = 50_000;

        /// <summary>Price threshold (roubles) above which loot is highlighted as important.</summary>
        public int LootImportantPrice { get; set; } = 200_000;

        /// <summary>Show prices as price-per-slot instead of total price.</summary>
        public bool LootPricePerSlot { get; set; } = false;

        /// <summary>Price source for loot values (0 = Best, 1 = Flea, 2 = Trader).</summary>
        public int LootPriceSource { get; set; } = 0;

        // ── Loot Category Toggles ──────────────────────────────────────────────

        /// <summary>Always show medical items (bypasses price filter).</summary>
        public bool LootShowMeds { get; set; } = false;

        /// <summary>Always show food/drink items (bypasses price filter).</summary>
        public bool LootShowFood { get; set; } = false;

        /// <summary>Always show backpacks (bypasses price filter).</summary>
        public bool LootShowBackpacks { get; set; } = false;

        /// <summary>Always show keys/keycards (bypasses price filter).</summary>
        public bool LootShowKeys { get; set; } = false;

        /// <summary>Always show wishlisted items (bypasses all filters).</summary>
        public bool LootShowWishlist { get; set; } = true;

        // ── Profiles ────────────────────────────────────────────────────────────

        /// <summary>Enable tarkov.dev profile lookups for human players (KD, hours, etc.).</summary>
        public bool ProfileLookups { get; set; } = true;

        // ── Memory Writes ───────────────────────────────────────────────────────

        /// <summary>Master toggle for all memory write features.</summary>
        public bool MemWritesEnabled { get; set; } = false;

        // ── Web Radar ───────────────────────────────────────────────────────────

        /// <summary>Enable the web radar HTTP server on startup.</summary>
        public bool WebRadarEnabled { get; set; } = false;

        /// <summary>HTTP port for the web radar server.</summary>
        public int WebRadarPort { get; set; } = 7224;

        /// <summary>Web radar update interval in milliseconds.</summary>
        public int WebRadarTickMs { get; set; } = 50;

        // ── Hotkeys ─────────────────────────────────────────────────────────────

        /// <summary>
        /// All configured hotkeys keyed by action ID (e.g. "BattleMode", "ZoomIn").
        /// Only enabled entries with a valid key code are active.
        /// </summary>
        public Dictionary<string, HotkeyEntry> Hotkeys { get; set; } = [];

        // ── Containers ──────────────────────────────────────────────────────────

        /// <summary>
        /// BSG IDs of the container types the user has selected to display.
        /// Empty = show none. Populated by the container selection UI.
        /// </summary>
        public List<string> SelectedContainers { get; set; } = [];

        /// <summary>Hide containers that have been searched/opened.</summary>
        public bool HideSearchedContainers { get; set; } = true;

        // ── Persistence ─────────────────────────────────────────────────────────

        /// <summary>
        /// Clamps all numeric properties to safe ranges, preventing corrupt/hand-edited
        /// config files from producing invalid state.
        /// </summary>
        private void Validate()
        {
            UIScale = Math.Clamp(UIScale, 0.5f, 3.0f);
            TargetFps = Math.Clamp(TargetFps, 30, 300);
            WindowWidth = Math.Clamp(WindowWidth, 800, 7680);
            WindowHeight = Math.Clamp(WindowHeight, 600, 4320);

            AimviewPlayerDistance = Math.Clamp(AimviewPlayerDistance, 1f, 2000f);
            AimviewLootDistance = Math.Clamp(AimviewLootDistance, 1f, 500f);
            AimviewEyeHeight = Math.Clamp(AimviewEyeHeight, 0f, 5f);
            AimviewZoom = Math.Clamp(AimviewZoom, 0.5f, 5.0f);
            AimlineLength = Math.Clamp(AimlineLength, 0, 500);

            DoorLootProximity = Math.Clamp(DoorLootProximity, 1f, 200f);

            LootMinPrice = Math.Max(LootMinPrice, 0);
            LootImportantPrice = Math.Max(LootImportantPrice, 0);
            LootPriceSource = Math.Clamp(LootPriceSource, 0, 2);

            WebRadarPort = Math.Clamp(WebRadarPort, 1024, 65535);
            WebRadarTickMs = Math.Clamp(WebRadarTickMs, 16, 1000);

            Hotkeys ??= [];
            SelectedContainers ??= [];

            if (string.IsNullOrWhiteSpace(DeviceStr))
                DeviceStr = "fpga";
        }

        /// <summary>
        /// Load config from disk. Returns a default instance if the file does not exist or is corrupt.
        /// All values are clamped to safe ranges after deserialization.
        /// </summary>
        public static SilkConfig Load()
        {
            SilkConfig cfg;
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    cfg = JsonSerializer.Deserialize<SilkConfig>(json) ?? new SilkConfig();
                    cfg.Validate();
                    Log.WriteLine("[SilkConfig] Config loaded OK.");
                    return cfg;
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
        /// Marks the config as dirty. The next call to <see cref="FlushIfDirty"/>
        /// (after the debounce interval) will persist it to disk.
        /// </summary>
        public void MarkDirty()
        {
            _dirty = true;
            Interlocked.Exchange(ref _dirtyTimestamp, Environment.TickCount64);
        }

        /// <summary>
        /// Persists the config to disk if it has been marked dirty and the debounce
        /// interval has elapsed. Call periodically from the render loop or a timer.
        /// </summary>
        public void FlushIfDirty()
        {
            if (!_dirty)
                return;
            if (Environment.TickCount64 - Interlocked.Read(ref _dirtyTimestamp) < DebounceSaveMs)
                return;
            _dirty = false;
            Save();
        }

        /// <summary>
        /// Save config to disk immediately.
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
