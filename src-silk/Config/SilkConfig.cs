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

    /// <summary>Move speed multiplier sub-config.</summary>
    public sealed class MoveSpeedConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Speed multiplier (e.g. 1.2 = 20% faster).</summary>
        [JsonPropertyName("multiplier")]
        public float Multiplier { get; set; } = 1.2f;
    }

    /// <summary>FullBright sub-config.</summary>
    public sealed class FullBrightConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Ambient brightness level [0..2].</summary>
        [JsonPropertyName("brightness")]
        public float Brightness { get; set; } = 1.0f;
    }

    /// <summary>Extended reach sub-config.</summary>
    public sealed class ExtendedReachConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Loot/door interact distance (default game: ~1.3m).</summary>
        [JsonPropertyName("distance")]
        public float Distance { get; set; } = 3.0f;
    }

    /// <summary>Long jump sub-config.</summary>
    public sealed class LongJumpConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Air control multiplier (higher = longer jumps).</summary>
        [JsonPropertyName("multiplier")]
        public float Multiplier { get; set; } = 2.0f;
    }

    /// <summary>Wide lean sub-config.</summary>
    public sealed class WideLeanConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>Lean amount (multiplied by 0.2 internally).</summary>
        [JsonPropertyName("amount")]
        public float Amount { get; set; } = 1.0f;
    }

    /// <summary>Per-feature memory write settings.</summary>
    public sealed class MemWritesConfig
    {
        [JsonPropertyName("noRecoil")]
        public bool NoRecoil { get; set; } = false;

        /// <summary>Recoil amount percent (0 = none, 100 = full).</summary>
        [JsonPropertyName("noRecoilAmount")]
        public int NoRecoilAmount { get; set; } = 0;

        /// <summary>Sway amount percent (0 = none, 100 = full).</summary>
        [JsonPropertyName("noSwayAmount")]
        public int NoSwayAmount { get; set; } = 0;

        [JsonPropertyName("noInertia")]
        public bool NoInertia { get; set; } = false;

        [JsonPropertyName("infStamina")]
        public bool InfStamina { get; set; } = false;

        [JsonPropertyName("nightVision")]
        public bool NightVision { get; set; } = false;

        [JsonPropertyName("thermalVision")]
        public bool ThermalVision { get; set; } = false;

        [JsonPropertyName("moveSpeed")]
        public MoveSpeedConfig MoveSpeed { get; set; } = new();

        [JsonPropertyName("fullBright")]
        public FullBrightConfig FullBright { get; set; } = new();

        [JsonPropertyName("noVisor")]
        public bool NoVisor { get; set; } = false;

        [JsonPropertyName("disableFrostbite")]
        public bool DisableFrostbite { get; set; } = false;

        [JsonPropertyName("disableInventoryBlur")]
        public bool DisableInventoryBlur { get; set; } = false;

        [JsonPropertyName("disableWeaponCollision")]
        public bool DisableWeaponCollision { get; set; } = false;

        [JsonPropertyName("extendedReach")]
        public ExtendedReachConfig ExtendedReach { get; set; } = new();

        [JsonPropertyName("fastDuck")]
        public bool FastDuck { get; set; } = false;

        [JsonPropertyName("longJump")]
        public LongJumpConfig LongJump { get; set; } = new();

        [JsonPropertyName("thirdPerson")]
        public bool ThirdPerson { get; set; } = false;

        [JsonPropertyName("instantPlant")]
        public bool InstantPlant { get; set; } = false;

        [JsonPropertyName("magDrills")]
        public bool MagDrills { get; set; } = false;

        [JsonPropertyName("muleMode")]
        public bool MuleMode { get; set; } = false;

        [JsonPropertyName("wideLean")]
        public WideLeanConfig WideLean { get; set; } = new();

        [JsonPropertyName("medPanel")]
        public bool MedPanel { get; set; } = false;

        [JsonPropertyName("owlMode")]
        public bool OwlMode { get; set; } = false;
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

        /// <summary>
        /// Use the advanced aimview mode that reads the game's real camera ViewMatrix
        /// via <see cref="CameraManager"/> for pixel-accurate W2S projection.
        /// When false (default), the aimview uses a synthetic camera built from
        /// the local player's position + rotation.
        /// </summary>
        public bool UseAdvancedAimview { get; set; } = false;

        /// <summary>Game monitor width (pixels) — used by CameraManager for W2S viewport math.</summary>
        public int GameMonitorWidth { get; set; } = 2560;

        /// <summary>Game monitor height (pixels) — used by CameraManager for W2S viewport math.</summary>
        public int GameMonitorHeight { get; set; } = 1440;

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

        /// <summary>Whether the Hideout panel is open.</summary>
        public bool ShowHideoutPanel { get; set; } = false;

        /// <summary>Whether the Quest Info panel is open.</summary>
        public bool ShowQuestPanel { get; set; } = false;

        // ── Hideout ─────────────────────────────────────────────────────────────

        /// <summary>Enable hideout stash/area reading when entering the hideout scene.</summary>
        public bool HideoutEnabled { get; set; } = true;

        /// <summary>Automatically refresh stash and area data on hideout entry.</summary>
        public bool HideoutAutoRefresh { get; set; } = true;

        // ── Exfils ──────────────────────────────────────────────────────────────

        /// <summary>Master toggle for exfil rendering on the radar.</summary>
        public bool ShowExfils { get; set; } = true;

        /// <summary>Hide exfils that are closed or not available to the local player.</summary>
        public bool HideInactiveExfils { get; set; } = true;

        // ── Quests ──────────────────────────────────────────────────────────────

        /// <summary>Master toggle for quest zone rendering on the radar.</summary>
        public bool ShowQuests { get; set; } = true;

        /// <summary>Only show quests required for Kappa container.</summary>
        public bool QuestKappaFilter { get; set; } = false;

        /// <summary>Show optional quest objectives.</summary>
        public bool QuestShowOptional { get; set; } = true;

        /// <summary>Show quest zone names on the radar.</summary>
        public bool QuestShowNames { get; set; } = true;

        /// <summary>Show quest zone distances on the radar.</summary>
        public bool QuestShowDistance { get; set; } = true;

        /// <summary>Quest IDs blacklisted from display (user-hidden).</summary>
        public List<string> QuestBlacklist { get; set; } = [];

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

        /// <summary>Per-feature memory write settings.</summary>
        public MemWritesConfig MemWrites { get; set; } = new();

        // ── Web Radar ───────────────────────────────────────────────────────────

        /// <summary>Enable the web radar HTTP server on startup.</summary>
        public bool WebRadarEnabled { get; set; } = false;

        /// <summary>HTTP port for the web radar server.</summary>
        public int WebRadarPort { get; set; } = 7224;

        /// <summary>Web radar update interval in milliseconds.</summary>
        public int WebRadarTickMs { get; set; } = 50;

        /// <summary>Enable UPnP/NAT-PMP automatic port forwarding for the web radar.</summary>
        public bool WebRadarUPnP { get; set; } = false;

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

            GameMonitorWidth = Math.Clamp(GameMonitorWidth, 640, 7680);
            GameMonitorHeight = Math.Clamp(GameMonitorHeight, 480, 4320);

            DoorLootProximity = Math.Clamp(DoorLootProximity, 1f, 200f);

            LootMinPrice = Math.Max(LootMinPrice, 0);
            LootImportantPrice = Math.Max(LootImportantPrice, 0);
            LootPriceSource = Math.Clamp(LootPriceSource, 0, 2);

            WebRadarPort = Math.Clamp(WebRadarPort, 1024, 65535);
            WebRadarTickMs = Math.Clamp(WebRadarTickMs, 16, 1000);

            Hotkeys ??= [];
            SelectedContainers ??= [];
            QuestBlacklist ??= [];

            MemWrites ??= new();
            MemWrites.MoveSpeed ??= new();
            MemWrites.FullBright ??= new();
            MemWrites.ExtendedReach ??= new();
            MemWrites.LongJump ??= new();
            MemWrites.WideLean ??= new();
            MemWrites.NoRecoilAmount  = Math.Clamp(MemWrites.NoRecoilAmount,  0, 100);
            MemWrites.NoSwayAmount    = Math.Clamp(MemWrites.NoSwayAmount,    0, 100);
            MemWrites.MoveSpeed.Multiplier = Math.Clamp(MemWrites.MoveSpeed.Multiplier, 0.5f, 5.0f);
            MemWrites.FullBright.Brightness = Math.Clamp(MemWrites.FullBright.Brightness, 0f, 2f);
            MemWrites.ExtendedReach.Distance = Math.Clamp(MemWrites.ExtendedReach.Distance, 1f, 20f);
            MemWrites.LongJump.Multiplier = Math.Clamp(MemWrites.LongJump.Multiplier, 1f, 10f);
            MemWrites.WideLean.Amount = Math.Clamp(MemWrites.WideLean.Amount, 0.1f, 5f);

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
