using eft_dma_radar;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.UI;
using eft_dma_radar.UI.LootFilters;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Config;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Misc.Data.EFT;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.LowLevel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using static eft_dma_radar.Tarkov.API.EFTProfileService;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using Size = System.Windows.Size;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Chams;
public static class ConfigManager
{
    private static readonly string ConfigDirectory = Program.ConfigPath.FullName;
    public static readonly string CustomConfigDirectory = Program.CustomConfigPath.FullName;
    private const string ConfigExtension = ".json";
    private const string LastSelectedConfigFile = "lastSelectedConfig.json";
    
    public static Config CurrentConfig { get; private set; }
    public static string CurrentConfigName { get; private set; }

    // Simple class to store last selected config info
    private class LastSelectedConfig
    {
        public string ConfigFilename { get; set; }
    }

    // Initialize config manager
    static ConfigManager()
    {
        // Ensure directory exists
        if (!Directory.Exists(CustomConfigDirectory))
        {
            Directory.CreateDirectory(CustomConfigDirectory);
        }

        // Try to load last selected config
        string configToLoad = GetLastSelectedConfig();
        // If no last selected config or it doesn't exist, use config-eft-v3.json
        if (string.IsNullOrEmpty(configToLoad)) 
        {
            configToLoad = "config-eft-v3.json";
        }

        var configPath = Path.Combine(CustomConfigDirectory, configToLoad);
        
        // If the config file doesn't exist, create it
        if (!File.Exists(configPath))
        {
            CurrentConfig = new Config 
            { 
                ConfigName = Path.GetFileNameWithoutExtension(configToLoad),
                Filename = configToLoad
            };
            SafeSaveConfig(CurrentConfig, configPath);
            SetLastSelectedConfig(configToLoad);
        }
        else
        {
            // Load the config
            CurrentConfig = LoadConfigFromFile(configPath);
        }

        CurrentConfigName = CurrentConfig.Filename;
        XMLogging.WriteLine($"[Config] Loaded config: {CurrentConfigName}");
    }

    // Get the last selected config filename
    private static string GetLastSelectedConfig()
    {
        var lastSelectedPath = Path.Combine(CustomConfigDirectory, LastSelectedConfigFile);
        
        try
        {
            if (File.Exists(lastSelectedPath))
            {
                var json = File.ReadAllText(lastSelectedPath);
                var lastSelected = JsonSerializer.Deserialize<LastSelectedConfig>(json);
                return lastSelected?.ConfigFilename;
            }
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error reading last selected config: {ex}");
        }
        
        return null;
    }

    // Set the last selected config filename
    private static void SetLastSelectedConfig(string configFilename)
    {
        var lastSelectedPath = Path.Combine(CustomConfigDirectory, LastSelectedConfigFile);
        
        try
        {
            var lastSelected = new LastSelectedConfig { ConfigFilename = configFilename };
            var json = JsonSerializer.Serialize(lastSelected);
            File.WriteAllText(lastSelectedPath, json);
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error saving last selected config: {ex}");
        }
    }

    // Get list of available configs
    public static List<Config> GetAvailableConfigs()
    {
        var configs = new List<Config>();

        try
        {
            var files = Directory.GetFiles(CustomConfigDirectory, $"*{ConfigExtension}")
                              .Where(f => !f.EndsWith(LastSelectedConfigFile))
                              .ToList();

            foreach (var file in files)
            {
                var config = LoadConfigFromFile(file);
                if (config != null)
                {
                    configs.Add(config);
                    XMLogging.WriteLine($"[Config] Found config: {config.Filename}");
                }
            }
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error getting config list: {ex}");
        }

        return configs;
    }

    public static Config LoadConfigFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<Config>(json);

            if (config != null)
            {
                // Initialize properties
                config.Filename = Path.GetFileName(path);

                if (string.IsNullOrEmpty(config.ConfigName))
                {
                    config.ConfigName = Path.GetFileNameWithoutExtension(path);
                }
            }
                //GeneralSettingsControl.ApplyConfig();
            
            return config;
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Failed to load config '{path}': {ex}");
            return null;
        }
    }
    
    private static bool SafeSaveConfig(Config config, string path)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Failed to save config '{path}': {ex}");
            return false;
        }
    }

    public static bool SaveConfigToFile(Config config, string path)
    {
        return SafeSaveConfig(config, path);
    }

    // Load a specific config
    public static bool LoadConfig(string configName)
    {
        try
        {
            if (string.IsNullOrEmpty(configName))
                return false;

            if (!configName.EndsWith(ConfigExtension, StringComparison.OrdinalIgnoreCase))
                configName += ConfigExtension;

            var configPath = Path.Combine(CustomConfigDirectory, configName);
            var newConfig = LoadConfigFromFile(configPath);

            if (newConfig != null)
            {
                CurrentConfig = newConfig;
                CurrentConfigName = newConfig.Filename;
                SetLastSelectedConfig(configName);
                return true;
            }
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error loading config {configName}: {ex}");
        }

        return false;
    }

    public static bool SaveAsNewConfig(string configName)
    {
        try
        {
            if (string.IsNullOrEmpty(configName))
                return false;
    
            if (!configName.EndsWith(ConfigExtension, StringComparison.OrdinalIgnoreCase))
                configName += ConfigExtension;
    
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(CurrentConfig, options);
            var configToSave = JsonSerializer.Deserialize<Config>(json, options);
            
            configToSave.Filename = configName;
            configToSave.ConfigName = Path.GetFileNameWithoutExtension(configName);
    
            var filePath = Path.Combine(CustomConfigDirectory, configName);
            SafeSaveConfig(configToSave, filePath);
            
            XMLogging.WriteLine($"[Config] Saved new config: {configName}");
            return true;
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error saving new config {configName}: {ex}");
            return false;
        }
    }
   
    public static bool DeleteConfig(string configName)
    {
        try
        {
            if (string.IsNullOrEmpty(configName))
                return false;

            if (!configName.EndsWith(ConfigExtension, StringComparison.OrdinalIgnoreCase))
                configName += ConfigExtension;

            var filePath = Path.Combine(CustomConfigDirectory, configName);

            if (File.Exists(filePath))
            {
                // Check if we're deleting the currently loaded config
                bool isCurrent = CurrentConfigName.Equals(configName, StringComparison.OrdinalIgnoreCase);
                
                File.Delete(filePath);

                if (!File.Exists(filePath))
                {
                    // If we deleted the current config, fall back to config-eft-v3.json
                    if (isCurrent)
                    {
                        var fallbackConfig = "config-eft-v3.json";
                        var fallbackPath = Path.Combine(CustomConfigDirectory, fallbackConfig);
                        
                        if (File.Exists(fallbackPath))
                        {
                            LoadConfig(fallbackConfig);
                        }
                        else
                        {
                            // Create new default config
                            CurrentConfig = new Config
                            {
                                ConfigName = "config-eft-v3",
                                Filename = "config-eft-v3.json"
                            };
                            CurrentConfigName = CurrentConfig.Filename;
                            SafeSaveConfig(CurrentConfig, fallbackPath);
                            SetLastSelectedConfig(fallbackConfig);
                        }
                    }
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            XMLogging.WriteLine($"[Config] Error deleting config {configName}: {ex}");
            return false;
        }
    }

    public static void ResetToDefault()
    {
        var defaultConfig = "config-eft-v3.json";
        var defaultPath = Path.Combine(CustomConfigDirectory, defaultConfig);
        
        if (File.Exists(defaultPath))
        {
            LoadConfig(defaultConfig);
        }
        else
        {
            CurrentConfig = new Config
            {
                ConfigName = "config-eft-v3",
                Filename = "config-eft-v3.json"
            };
            CurrentConfigName = CurrentConfig.Filename;
            SafeSaveConfig(CurrentConfig, defaultPath);
            SetLastSelectedConfig(defaultConfig);
        }
    }
}
namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Global Program Configuration (Config.json)
    /// </summary>
    public sealed class Config : IConfig
    {
        #region ISharedConfig
        [JsonIgnore]
        public bool MemWritesEnabled => this.MemWrites.MemWritesEnabled;
        [JsonIgnore]
        public LowLevelCache LowLevelCache => this.Cache.LowLevel;
        [JsonIgnore]
        public ChamsConfig ChamsConfig => this.MemWrites.Chams;    
        [JsonIgnore]
        public bool AdvancedMemWrites => this.MemWrites.AdvancedMemWrites;           
        #endregion

        /// <summary>
        /// Toolbar position configuration
        /// </summary>
        [JsonPropertyName("toolbarPosition")]
        [JsonInclude]
        public ToolbarPositionConfig ToolbarPosition { get; set; } = new ToolbarPositionConfig();

        /// <summary>
        /// Panel positions configuration
        /// </summary>
        [JsonPropertyName("panelPositions")]
        [JsonInclude]
        public PanelPositionsConfig PanelPositions { get; set; } = new PanelPositionsConfig();

        [JsonPropertyName("expanderStates")]
        [JsonInclude]
        public ExpanderStatesConfig ExpanderStates { get; set; } = new();

        /// <summary>
        /// Config Name.
        /// </summary>
        [JsonPropertyName("configName")]
        public string ConfigName { get; set; } = "config-eft-v3";

        /// <summary>
        /// Target FPS for the 2D Radar.
        /// </summary>
        [JsonPropertyName("radarTargetFPS")]
        public int RadarTargetFPS { get; set; } = 60;

        /// <summary>
        /// True if there should be a mandatory delay between memory reads on the Realtime Thread.
        /// </summary>
        [JsonPropertyName("ratelimitRealtimeReads")]
        public bool RatelimitRealtimeReads { get; set; } = false;

        /// <summary>
        /// UI Scale Value (0.5-2.0 , default: 1.0)
        /// </summary>
        [JsonPropertyName("uiScale")]
        public float UIScale { get; set; } = 1.0f;

        /// <summary>
        /// Enable 'Battle Mode', hides all non-essential information.
        /// </summary>
        [JsonPropertyName("battleMode")]
        public bool BattleMode { get; set; } = false;

        /// <summary>
        /// Size of the Radar Window.
        /// </summary>
        [JsonPropertyName("windowSize")]
        public Size WindowSize { get; set; } = new(1280, 720);

        /// <summary>
        /// Window is maximized.
        /// </summary>
        [JsonPropertyName("windowMaximized")]
        public bool WindowMaximized { get; set; }

        /// <summary>
        /// Last used 'Zoom' level.
        /// </summary>
        [JsonPropertyName("lastZoom")]
        public int Zoom { get; set; } = 100;

        /// <summary>
        /// Enables processing loot on map.
        /// </summary>
        [JsonPropertyName("processLoot")]
        public bool ProcessLoot { get; set; } = true;

        /// <summary>
        /// Quest Helper Cfg
        /// </summary>
        [JsonPropertyName("questHelperCfg")]
        [JsonInclude]
        public QuestHelperConfig QuestHelper { get; private set; } = new();

        /// <summary>
        /// Shows Player Info Tab/Pane in the top right corner of radar.
        /// </summary>
        [JsonPropertyName("showInfoTab")]
        public bool ShowInfoTab { get; set; } = true;

        /// <summary>
        /// Shows Debug Info Tab/Pane.
        /// </summary>
        [JsonPropertyName("showDebugWidget")]
        public bool ShowDebugWidget { get; set; } = false;

        /// <summary>
        /// Shows Loot Info Tab/Pane.
        /// </summary>
        [JsonPropertyName("showLootInfoWidget")]
        public bool ShowLootInfoWidget { get; set; } = false;

        /// <summary>
        /// Shows Quest Info Tab/Pane.
        /// </summary>
        [JsonPropertyName("showQuestInfoWidget")]
        public bool ShowQuestInfoWidget { get; set; } = false;

        /// <summary>
        /// Enables ESP Widget window in Main Window.
        /// </summary>
        [JsonPropertyName("aimviewEnabled")]
        public bool AimviewWidgetEnabled { get; set; } = true;

        /// <summary>
        /// Connects grouped players together via a semi-transparent line.
        /// </summary>
        [JsonPropertyName("connectGroups")]
        public bool ConnectGroups { get; set; } = true;

        /// <summary>
        /// Replace all names with '<Hidden>'
        /// </summary>
        [JsonPropertyName("maskNames")]
        public bool MaskNames { get; set; } = true;

        /// <summary>
        /// Replace all names with '<Hidden>'
        /// </summary>
        [JsonPropertyName("playersOnTop")]
        public bool PlayersOnTop { get; set; } = true;

        /// <summary>
        /// Minimum loot value (rubles) to display 'normal loot' on map.
        /// </summary>
        [JsonPropertyName("minLootValue")]
        public int MinLootValue { get; set; } = 50000;

        /// <summary>
        /// Minimum loot value (rubles) to display a corpse on the map.
        /// </summary>
        [JsonPropertyName("minCorpseValue")]
        public int MinCorpseValue { get; set; } = 50000;

        /// <summary>
        /// Show Loot by "Price per Slot".
        /// </summary>
        [JsonPropertyName("lootPPS")]
        public bool LootPPS { get; set; }
        
        /// <summary>
        /// Loot Price Mode.
        /// </summary>
        [JsonPropertyName("lootPriceMode")]
        public LootPriceMode LootPriceMode { get; set; } = LootPriceMode.FleaMarket;

        /// <summary>
        /// Show loot on the player's wishlist (manual only).
        /// </summary>
        [JsonPropertyName("lootWishList")]
        public bool LootWishlist { get; set; } = false;

        /// <summary>
        /// Show corpse markers (X) on radar
        /// </summary>
        [JsonPropertyName("showCorpseMarkers")]
        public bool ShowCorpseMarkers { get; set; } = true;

        /// <summary>
        /// Minimum loot value (rubles) to display 'important loot' on map.
        /// </summary>
        [JsonPropertyName("minImportantLootValue")]
        public int MinValuableLootValue { get; set; } = 200000;

        /// <summary>
        /// FPGA Read Algorithm
        /// </summary>
        [JsonPropertyName("fpgaAlgo")]
        public FpgaAlgo FpgaAlgo { get; set; } = FpgaAlgo.Auto;

        /// <summary>
        /// Use a Memory Map for FPGA DMA Connection.
        /// </summary>
        [JsonPropertyName("enableMemMap")]
        public bool MemMapEnabled { get; set; } = true;

        /// <summary>
        /// Game PC Monitor Resolution Width
        /// </summary>
        [JsonPropertyName("monitorWidth")]
        public int MonitorWidth { get; set; } = 1920;

        /// <summary>
        /// Game PC Monitor Resolution Height
        /// </summary>
        [JsonPropertyName("monitorHeight")]
        public int MonitorHeight { get; set; } = 1080;

        /// <summary>
        /// All defined Radar Colors.
        /// </summary>
        [JsonPropertyName("radarColors")]
        public Dictionary<RadarColorOption, string> Colors { get; set; } = RadarColorOptions.GetDefaultColors();

        /// <summary>
        /// All defined Interface Colors.
        /// </summary>
        [JsonPropertyName("interfaceColors")]
        public Dictionary<InterfaceColorOption, string> InterfaceColors { get; set; } = InterfaceColorOptions.GetDefaultColors();

        /// <summary>
        /// Player type-specific display settings
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("playerTypeSettings")]
        public PlayerTypeSettingsConfig PlayerTypeSettings { get; set; } = new PlayerTypeSettingsConfig();

        /// <summary>
        /// Entity type-specific display settings
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("entityTypeSettings")]
        public EntityTypeSettingsConfig EntityTypeSettings { get; set; } = new EntityTypeSettingsConfig();

        /// <summary>
        /// DMA Toolkit (Write Features) Config.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("dmaToolkit")]
        public MemWritesConfig MemWrites { get; private set; } = new();

        /// <summary>
        /// ESP Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("esp")]
        public ESPConfig ESP { get; private set; } = new();

        /// <summary>
        /// Widgets Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("widgets")]
        public WidgetsConfig Widgets { get; private set; } = new();

        /// <summary>
        /// ESP Widgets Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("widgetsESP")]
        public ESPWidgetsConfig ESPWidgets { get; private set; } = new();

        /// <summary>
        /// Hotkeys Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("hotKeys")]
        public HotkeyConfig HotKeys { get; private set; } = new();
        /// <summary>
        /// Web Radar Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("webRadar")]
        public WebRadarConfig WebRadar { get; set; } = new();

        /// <summary>
        /// Containers configuration.
        /// </summary>
        [JsonPropertyName("containers")]
        public ContainersConfig Containers { get; set; } = new();

        /// <summary>
        /// Contains cache data between program sessions.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("R9tQvX5")]
        public PersistentCache Cache { get; set; } = new();

        #region Config Interface

        /// <summary>
        /// Filename of this Config File (not full path).
        /// </summary>
        [JsonIgnore] 
        public string Filename { get; set; } = "config-eft-v3.json";

        /// <summary>
        /// The eft profile service, false if wanting tarkov.dev, true if wanting eft-api.tech
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("alternateProfileService")]
        public bool AlternateProfileService { get; set; } = false;

        /// <summary>
        /// Send anonymous data to fd-mambo server to count amoutn of users. A simple ping, no IP or personal info is stored. It creates and uses a uniqe ID number.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("sendAnonymousUsage")]
        public bool SendAnonymousUsage { get; set; } = false;

        /// <summary>
        /// The maximum amount of requests to send per minute to eft-api.tech (5 = free tier, 60+ = premium tier)
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("requestsPerMin")]
        public int RequestsPerMin { get; set; } = 60;

        [JsonIgnore] private static readonly Lock _syncRoot = new();

        [JsonIgnore]
        private FileInfo _configFile => new(Path.Combine(Program.ConfigPath.FullName, Filename));

        [JsonIgnore]
        private FileInfo _tempFile => new(Path.Combine(Program.ConfigPath.FullName, Filename + ".tmp"));

        public static Config Load(string filename)
        {
            lock (_syncRoot)
            {
                try
                {
                    Config config = new Config();
                    config.Filename = filename;

                    // Always load from custom config directory now
                    FileInfo configFile = new FileInfo(Path.Combine(Program.CustomConfigPath.FullName, filename));
                    var tempFile = new FileInfo(configFile.FullName + ".tmp");

                    if (configFile.Exists)
                    {
                        string json = null;
                        try
                        {
                            json = File.ReadAllText(configFile.FullName);
                        }
                        catch
                        {
                            if (tempFile.Exists)
                            {
                                try
                                {
                                    json = File.ReadAllText(tempFile.FullName);
                                }
                                catch
                                {
                                    json = null;
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(json))
                        {
                            try
                            {
                                var options = new JsonSerializerOptions
                                {
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                                    IgnoreReadOnlyProperties = true,
                                    ReadCommentHandling = JsonCommentHandling.Skip,
                                    AllowTrailingCommas = true,
                                    PropertyNameCaseInsensitive = true
                                };

                                config = JsonSerializer.Deserialize<Config>(json, options);
                                config.Filename = filename;
                                config.ConfigName = Path.GetFileNameWithoutExtension(filename);

                                // Don't modify IsDefaultConfig here - preserve whatever was saved
                                EnsureComplexObjectsInitialized(config);
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"Error deserializing config: {ex.Message}");

                                config = new Config();
                                config.Filename = filename;
                                config.ConfigName = Path.GetFileNameWithoutExtension(filename);

                                if (!(ex is JsonException))
                                {
                                    MessageBox.Show(
                                        $"Config File Error: {ex.Message}\n\n" +
                                        $"Default settings will be used.");
                                }
                            }
                        }

                        if (config == null)
                        {
                            config = new Config();
                            config.Filename = filename;
                            config.ConfigName = Path.GetFileNameWithoutExtension(filename);
                            SaveInternal(config);
                        }
                    }
                    else
                    {
                        // Create new config if file doesn't exist
                        config = new Config();
                        config.Filename = filename;
                        config.ConfigName = Path.GetFileNameWithoutExtension(filename);

                        // Only set as default if there are no other configs
                        var existingConfigs = Directory.GetFiles(Program.CustomConfigPath.FullName, "*.json");

                        SaveInternal(config);
                    }

                    return config;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"CRITICAL ERROR Loading Config: {ex.Message}");
                    var config = new Config();
                    config.Filename = filename;
                    config.ConfigName = Path.GetFileNameWithoutExtension(filename);
                    return config;
                }
            }
        }

        /// <summary>
        /// Ensures all complex objects are properly initialized
        /// </summary>
        public static void EnsureComplexObjectsInitialized(Config config)
        {
            if (config.ToolbarPosition == null)
                config.ToolbarPosition = new ToolbarPositionConfig();

            if (config.PanelPositions == null)
                config.PanelPositions = new PanelPositionsConfig();

            if (config.ExpanderStates == null)
                config.ExpanderStates = new ExpanderStatesConfig();

            if (config.PlayerTypeSettings == null)
                config.PlayerTypeSettings = new PlayerTypeSettingsConfig();

            if (config.EntityTypeSettings == null)
                config.EntityTypeSettings = new EntityTypeSettingsConfig();

            if (config.QuestHelper == null)
                config.QuestHelper = new QuestHelperConfig();

            if (config.MemWrites == null)
                config.MemWrites = new MemWritesConfig();

            if (config.ESP == null)
                config.ESP = new ESPConfig();

            if (config.Widgets == null)
                config.Widgets = new WidgetsConfig();

            if (config.ESPWidgets == null)
                config.ESPWidgets = new ESPWidgetsConfig();

            if (config.HotKeys == null)
                config.HotKeys = new HotkeyConfig();

            if (config.WebRadar == null)
                config.WebRadar = new WebRadarConfig();

            if (config.Containers == null)
                config.Containers = new ContainersConfig();

            if (config.Cache == null)
                config.Cache = new PersistentCache();

            if (config.Colors == null)
                config.Colors = RadarColorOptions.GetDefaultColors();

            if (config.InterfaceColors == null)
                config.InterfaceColors = InterfaceColorOptions.GetDefaultColors();

            if (config.PanelPositions != null)
            {
                if (config.PanelPositions.GeneralSettings == null)
                    config.PanelPositions.GeneralSettings = new PanelPositionConfig();

                if (config.PanelPositions.LootSettings == null)
                    config.PanelPositions.LootSettings = new PanelPositionConfig();

                if (config.PanelPositions.MemoryWriting == null)
                    config.PanelPositions.MemoryWriting = new PanelPositionConfig();

                if (config.PanelPositions.Watchlist == null)
                    config.PanelPositions.Watchlist = new PanelPositionConfig();

                if (config.PanelPositions.PlayerHistory == null)
                    config.PanelPositions.PlayerHistory = new PanelPositionConfig();

                if (config.PanelPositions.ESP == null)
                    config.PanelPositions.ESP = new PanelPositionConfig();

                if (config.PanelPositions.LootFilter == null)
                    config.PanelPositions.LootFilter = new PanelPositionConfig();

                if (config.PanelPositions.PlayerPreview == null)
                    config.PanelPositions.PlayerPreview = new PanelPositionConfig();

                if (config.PanelPositions.MapSetup == null)
                    config.PanelPositions.MapSetup = new PanelPositionConfig();
            }

            if (config.ExpanderStates != null)
            {
                if (config.ExpanderStates.ExpanderStates == null)
                    config.ExpanderStates.ExpanderStates = new Dictionary<string, bool>();
            }

            if (config.PlayerTypeSettings != null)
            {
                if (config.PlayerTypeSettings.Settings == null)
                    config.PlayerTypeSettings.Settings = new Dictionary<string, PlayerTypeSettings>();

                config.PlayerTypeSettings.InitializeDefaults();
            }

            if (config.EntityTypeSettings != null)
            {
                if (config.EntityTypeSettings.Settings == null)
                    config.EntityTypeSettings.Settings = new Dictionary<string, EntityTypeSettings>();

                config.EntityTypeSettings.InitializeDefaults();
            }

            if (config.QuestHelper != null)
            {
                if (config.QuestHelper.BlacklistedQuests == null)
                    config.QuestHelper.BlacklistedQuests = new HashSet<string>();
            }

            if (config.MemWrites != null)
            {
                if (config.MemWrites.Aimbot == null)
                    config.MemWrites.Aimbot = new AimbotConfig();

                if (config.MemWrites.WideLean == null)
                    config.MemWrites.WideLean = new WideLeanConfig();

                if (config.MemWrites.Aimbot != null)
                {
                    if (config.MemWrites.Aimbot.SilentAim == null)
                        config.MemWrites.Aimbot.SilentAim = new SilentAimConfig();

                    if (config.MemWrites.Aimbot.RandomBone == null)
                        config.MemWrites.Aimbot.RandomBone = new AimbotRandomBoneConfig();
                }
            }

            if (config.ESP != null)
            {
                if (config.ESP.Colors == null)
                    config.ESP.Colors = EspColorOptions.GetDefaultColors();

                if (config.ESP.Crosshair == null)
                    config.ESP.Crosshair = new ESPCrosshairOptions();

                if (config.ESP.MiniRadar == null)
                    config.ESP.MiniRadar = new ESPMiniRadarOptions();

                if (config.ESP.PlayerTypeESPSettings == null)
                    config.ESP.PlayerTypeESPSettings = new PlayerTypeSettingsESPConfig();

                if (config.ESP.EntityTypeESPSettings == null)
                    config.ESP.EntityTypeESPSettings = new EntityTypeSettingsESPConfig();

                if (config.ESP.PlayerTypeESPSettings != null)
                {
                    if (config.ESP.PlayerTypeESPSettings.Settings == null)
                        config.ESP.PlayerTypeESPSettings.Settings = new Dictionary<string, PlayerTypeSettingsESP>();

                    config.ESP.PlayerTypeESPSettings.InitializeDefaults();
                }

                if (config.ESP.EntityTypeESPSettings != null)
                {
                    if (config.ESP.EntityTypeESPSettings.Settings == null)
                        config.ESP.EntityTypeESPSettings.Settings = new Dictionary<string, EntityTypeSettingsESP>();

                    config.ESP.EntityTypeESPSettings.InitializeDefaults();
                }
            }

            if (config.Widgets != null)
            {
                try { var temp = config.Widgets.AimviewLocation; }
                catch { config.Widgets.AimviewLocation = new SKRect(0, 0, 300, 300); }

                try { var temp = config.Widgets.PlayerInfoLocation; }
                catch { config.Widgets.PlayerInfoLocation = new SKRect(0, 0, 300, 300); }

                try { var temp = config.Widgets.DebugInfoLocation; }
                catch { config.Widgets.DebugInfoLocation = new SKRect(0, 0, 300, 300); }

                try { var temp = config.Widgets.LootInfoLocation; }
                catch { config.Widgets.LootInfoLocation = new SKRect(0, 0, 300, 300); }
            }

            if (config.ESPWidgets != null)
            {
                try { var temp = config.ESPWidgets.QuestInfoLocation; }
                catch { config.ESPWidgets.QuestInfoLocation = new SKRect(0, 0, 300, 300); }
            }

            if (config.Containers != null)
            {
                if (config.Containers.Selected == null)
                    config.Containers.Selected = new List<string>();
            }
        }

        /// <summary>
        /// Save this Config Instance to Disk.
        /// </summary>
        public void Save()
        {
            lock (_syncRoot)
            {
                try
                {
                    SaveInternal(this);
                }
                catch (Exception ex)
                {
                    throw new IOException($"[Save] ERROR Saving Config: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Save this Config Instance to Disk asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task SaveAsync() => await Task.Run(Save);

        private static void SaveInternal(Config config)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var json = JsonSerializer.Serialize(config, options);

                var configFile = new FileInfo(Path.Combine(Program.CustomConfigPath.FullName, config.Filename));
                var tempFile = new FileInfo(Path.Combine(Program.CustomConfigPath.FullName, config.Filename + ".tmp"));

                File.WriteAllText(tempFile.FullName, json);
                tempFile.CopyTo(configFile.FullName, true);

                try
                {
                    tempFile.Delete();
                }
                catch { }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[SaveInternal] Error saving config: {ex.Message}");
                throw;
            }
        }
        #endregion
    } 
    /// <summary>
    /// Configuration for panel positions
    /// </summary>
    public sealed class PanelPositionsConfig
    {
        /// <summary>
        /// General settings panel position
        /// </summary>
        [JsonPropertyName("generalSettings")]
        public PanelPositionConfig GeneralSettings { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// Loot settings panel position
        /// </summary>
        [JsonPropertyName("lootSettings")]
        public PanelPositionConfig LootSettings { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// Memory writing panel position
        /// </summary>
        [JsonPropertyName("memoryWriting")]
        public PanelPositionConfig MemoryWriting { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// ESP panel position
        /// </summary>
        [JsonPropertyName("esp")]
        public PanelPositionConfig ESP { get; set; } = new PanelPositionConfig();
        /// <summary>
        /// Watchlist panel position
        /// </summary>
        [JsonPropertyName("watchlist")]
        public PanelPositionConfig Watchlist { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// Player history panel position
        /// </summary>
        [JsonPropertyName("playerHistory")]
        public PanelPositionConfig PlayerHistory { get; set; } = new PanelPositionConfig();
        /// <summary>
        /// Loot filter panel position
        /// </summary>
        [JsonPropertyName("lootFilter")]
        public PanelPositionConfig LootFilter { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// Player preview panel position
        /// </summary>
        [JsonPropertyName("playerPreview")]
        public PanelPositionConfig PlayerPreview { get; set; } = new PanelPositionConfig();

        /// <summary>
        /// Map setup panel position
        /// </summary>
        [JsonPropertyName("mapSetup")]
        public PanelPositionConfig MapSetup { get; set; } = new PanelPositionConfig();
    }

    /// <summary>
    /// Configuration for toolbar position
    /// </summary>
    public sealed class ToolbarPositionConfig
    {
        [JsonPropertyName("left")]
        public double Left { get; set; }
        [JsonPropertyName("top")]
        public double Top { get; set; }

        public static ToolbarPositionConfig FromToolbar(Border toolbar)
        {
            var left = Canvas.GetLeft(toolbar);
            var top = Canvas.GetTop(toolbar);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            return new ToolbarPositionConfig
            {
                Left = left,
                Top = top,
            };
        }

        public void ApplyToToolbar(Border toolbar)
        {
            if (Left >= 0 && Top >= 0)
            {
                Canvas.SetLeft(toolbar, Left);
                Canvas.SetTop(toolbar, Top);

                toolbar.ClearValue(FrameworkElement.WidthProperty);
                toolbar.ClearValue(FrameworkElement.HeightProperty);
            }
            else
            {
                Canvas.SetLeft(toolbar, 20);
                Canvas.SetTop(toolbar, 5);

                toolbar.ClearValue(FrameworkElement.WidthProperty);
                toolbar.ClearValue(FrameworkElement.HeightProperty);
            }
        }
    }

    /// <summary>
    /// Class to store panel positions and sizes
    /// </summary>
    public sealed class PanelPositionConfig
    {
        [JsonPropertyName("left")]
        public double Left { get; set; }

        [JsonPropertyName("top")]
        public double Top { get; set; }

        [JsonPropertyName("width")]
        public double Width { get; set; }

        [JsonPropertyName("height")]
        public double Height { get; set; }

        [JsonPropertyName("visible")]
        public bool Visible { get; set; }

        public static PanelPositionConfig FromPanel(FrameworkElement panel, Canvas canvas)
        {
            var left = Canvas.GetLeft(panel);
            var top = Canvas.GetTop(panel);

            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            return new PanelPositionConfig
            {
                Left = left,
                Top = top,
                Width = panel.Width > 0 ? panel.Width : panel.ActualWidth,
                Height = panel.Height > 0 ? panel.Height : panel.ActualHeight,
                Visible = panel.Visibility == Visibility.Visible
            };
        }

        public void ApplyToPanel(FrameworkElement panel, Canvas canvas)
        {
            if (Left >= 0 && Top >= 0)
            {
                Canvas.SetLeft(panel, Left);
                Canvas.SetTop(panel, Top);

                if (Width > 0)
                    panel.Width = Width;

                if (Height > 0)
                    panel.Height = Height;
            }
            else
            {
                Canvas.SetLeft(panel, 20);
                Canvas.SetTop(panel, 20);
            }

            panel.Visibility = Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Configuration for saving expander states across all panels
    /// </summary>
    public sealed class ExpanderStatesConfig
    {
        /// <summary>
        /// Dictionary of panel expander states
        /// Key: PanelName:ExpanderName (e.g. "Watchlist:WatchlistManagement")
        /// Value: IsExpanded state
        /// </summary>
        [JsonPropertyName("expanderStates")]
        public Dictionary<string, bool> ExpanderStates { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Get the unique key for an expander
        /// </summary>
        public static string GetExpanderKey(string panelName, string expanderName)
        {
            return $"{panelName}:{expanderName}";
        }

        /// <summary>
        /// Get expander state from config
        /// </summary>
        public bool GetExpanderState(string panelName, string expanderName, bool defaultState = true)
        {
            var key = GetExpanderKey(panelName, expanderName);
            var state = ExpanderStates.TryGetValue(key, out bool value) ? value : defaultState;

            return state;
        }

        /// <summary>
        /// Save expander state to config
        /// </summary>
        public void SetExpanderState(string panelName, string expanderName, bool isExpanded)
        {
            var key = GetExpanderKey(panelName, expanderName);
            ExpanderStates[key] = isExpanded;
        }
    }

    /// <summary>
    /// Configuration for player type-specific display settings
    /// </summary>
    public sealed class PlayerTypeSettingsConfig
    {
        /// <summary>
        /// Settings for each player type
        /// </summary>
        [JsonPropertyName("playerTypeSettings")]
        public Dictionary<string, PlayerTypeSettings> Settings { get; set; } = new Dictionary<string, PlayerTypeSettings>();

        /// <summary>
        /// Initialize default settings for all player types
        /// </summary>
        public void InitializeDefaults()
        {
            var playerTypes = new List<string>();

            foreach (PlayerType type in Enum.GetValues(typeof(PlayerType)))
            {
                if (type != PlayerType.Default)
                    playerTypes.Add(type.ToString());
            }

            playerTypes.Add("LocalPlayer");
            playerTypes.Add("AimbotLocked");
            playerTypes.Add("Focused");

            playerTypes.Sort();

            foreach (var type in playerTypes)
            {
                if (!Settings.ContainsKey(type))
                    Settings[type] = new PlayerTypeSettings();
            }
        }

        /// <summary>
        /// Get settings for a specific player type, create default if not exists
        /// </summary>
        public PlayerTypeSettings GetSettings(string playerType)
        {
            if (!Settings.ContainsKey(playerType))
                Settings[playerType] = new PlayerTypeSettings();

            return Settings[playerType];
        }
    }
    
    /// <summary>
    /// Settings for a specific player type
    /// </summary>
    public sealed class PlayerTypeSettings
    {
        /// <summary>
        /// Information to display (Name, Distance, Height)
        /// </summary>
        [JsonPropertyName("information")]
        [JsonInclude]
        public HashSet<string> Information { get; set; } = new HashSet<string>
        {
            "Name",
            "Distance",
            "Weapon",
            "Ammo Type"
        };

        /// <summary>
        /// Player type render distance
        /// </summary>
        [JsonPropertyName("renderDistance")]
        public int RenderDistance { get; set; } = 1500;

        /// <summary>
        /// Length of the aimline
        /// </summary>
        [JsonPropertyName("aimlineLength")]
        public int AimlineLength { get; set; } = 15;

        /// <summary>
        /// Minimum kd required to display kd
        /// </summary>
        [JsonPropertyName("minimumKD")]
        public float MinKD { get; set; } = 1f;

        /// <summary>
        /// Show up/down arrows instead of numerical height
        /// </summary>
        [JsonPropertyName("heightIndicator")]
        public bool HeightIndicator { get; set; } = false;

        /// <summary>
        /// Asterisk if important / quest item is on player
        /// </summary>
        [JsonPropertyName("importantIndicator")]
        public bool ImportantIndicator { get; set; } = true;

        /// <summary>
        /// Display important items
        /// </summary>
        [JsonPropertyName("showImportantLoot")]
        public bool ShowImportantLoot { get; set; } = true;

        /// <summary>
        /// Aimline extends to show player looking in your direction
        /// </summary>
        [JsonPropertyName("highAlert")]
        public bool HighAlert { get; set; } = true;

        [JsonIgnore]
        public bool ShowName => Information.Contains("Name");

        [JsonIgnore]
        public bool ShowKD => Information.Contains("KD");

        [JsonIgnore]
        public bool ShowDistance => Information.Contains("Distance");

        [JsonIgnore]
        public bool ShowHeight => Information.Contains("Height");

        [JsonIgnore]
        public bool ShowHealth => Information.Contains("Health");

        [JsonIgnore]
        public bool ShowLevel => Information.Contains("Level");

        [JsonIgnore]
        public bool ShowWeapon => Information.Contains("Weapon");

        [JsonIgnore]
        public bool ShowAmmoType => Information.Contains("Ammo Type");

        [JsonIgnore]
        public bool ShowGroupID => Information.Contains("Group");

        [JsonIgnore]
        public bool ShowADS => Information.Contains("ADS");

        [JsonIgnore]
        public bool ShowThermal => Information.Contains("Thermal");

        [JsonIgnore]
        public bool ShowNVG => Information.Contains("Night Vision");

        [JsonIgnore]
        public bool ShowUBGL => Information.Contains("UBGL");

        [JsonIgnore]
        public bool ShowValue => Information.Contains("Value");

        [JsonIgnore]
        public bool ShowTag => Information.Contains("Tag");
    }

    /// <summary>
    /// Configuration for player type-specific ESP settings
    /// </summary>
    public sealed class PlayerTypeSettingsESPConfig
    {
        /// <summary>
        /// Settings for each player type
        /// </summary>
        [JsonPropertyName("playerTypeESPSettings")]
        public Dictionary<string, PlayerTypeSettingsESP> Settings { get; set; } = new Dictionary<string, PlayerTypeSettingsESP>();

        /// <summary>
        /// Initialize default settings for all player types
        /// </summary>
        public void InitializeDefaults()
        {
            var playerTypes = new List<string>();

            foreach (PlayerType type in Enum.GetValues(typeof(PlayerType)))
            {
                if (type != PlayerType.Default)
                    playerTypes.Add(type.ToString());
            }

            playerTypes.Add("AimbotLocked");
            playerTypes.Add("Focused");
            playerTypes.Sort();

            foreach (var type in playerTypes)
            {
                if (!Settings.ContainsKey(type))
                    Settings[type] = new PlayerTypeSettingsESP();
            }
        }

        /// <summary>
        /// Get settings for a specific player type, create default if not exists
        /// </summary>
        public PlayerTypeSettingsESP GetSettings(string playerType)
        {
            if (!Settings.ContainsKey(playerType))
                Settings[playerType] = new PlayerTypeSettingsESP();

            return Settings[playerType];
        }
    }

    /// <summary>
    /// Settings for a specific player type in ESP
    /// </summary>
    public sealed class PlayerTypeSettingsESP
    {
        /// <summary>
        /// Information to display (Name, Distance, Health, etc.)
        /// </summary>
        [JsonPropertyName("information")]
        [JsonInclude]
        public HashSet<string> Information { get; set; } = new HashSet<string>
        {
            "Name",
            "Distance",
            "Weapon",
            "Ammo Type"
        };

        /// <summary>
        /// Render mode for this player type
        /// </summary>
        [JsonPropertyName("renderMode")]
        public ESPPlayerRenderMode RenderMode { get; set; } = ESPPlayerRenderMode.Bones;

        /// <summary>
        /// Draw line to players looking at you, draw red border around screen if they're not in FOV
        /// </summary>
        [JsonPropertyName("highAlert")]
        public bool HighAlert { get; set; } = true;

        /// <summary>
        /// Important / quest item indication
        /// </summary>
        [JsonPropertyName("importantIndicator")]
        public bool ImportantIndicator { get; set; } = true;

        /// <summary>
        /// Display important items
        /// </summary>
        [JsonPropertyName("showImportantLoot")]
        public bool ShowImportantLoot { get; set; } = true;

        /// <summary>
        /// Player type render distance
        /// </summary>
        [JsonPropertyName("renderDistance")]
        public int RenderDistance { get; set; } = 1500;

        /// <summary>
        /// Minimum kd required to display kd
        /// </summary>
        [JsonPropertyName("minimumKD")]
        public float MinKD { get; set; } = 1f;

        /// <summary>
        /// Display name in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowName => Information.Contains("Name");

        /// <summary>
        /// Display kd in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowKD => Information.Contains("KD");

        /// <summary>
        /// Display distance in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowDistance => Information.Contains("Distance");

        /// <summary>
        /// Display health status in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowHealth => Information.Contains("Health");

        /// <summary>
        /// Display weapon in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowWeapon => Information.Contains("Weapon");

        /// <summary>
        /// Display ammo type in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowAmmoType => Information.Contains("Ammo Type");

        /// <summary>
        /// Display ADS status in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowADS => Information.Contains("ADS");

        /// <summary>
        /// Display thermal status in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowThermal => Information.Contains("Thermal");

        /// <summary>
        /// Display UBGL status in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowUBGL => Information.Contains("UBGL");

        /// <summary>
        /// Display NVG status in ESP
        /// </summary>
        [JsonIgnore]
        public bool ShowNVG => Information.Contains("Night Vision");
    }

    /// <summary>
    /// Configuration for entity type-specific display settings
    /// </summary>
    public sealed class EntityTypeSettingsConfig
    {
        /// <summary>
        /// Settings for each entity type
        /// </summary>
        [JsonPropertyName("entityTypeSettings")]
        public Dictionary<string, EntityTypeSettings> Settings { get; set; } = new Dictionary<string, EntityTypeSettings>();

        /// <summary>
        /// Initialize default settings for all entity types
        /// </summary>
        public void InitializeDefaults()
        {
            var entityTypes = new List<string>
            {
                "Airdrop",
                "Corpse",
                "RegularLoot",
                "ImportantLoot",
                "QuestItem",
                "Switch",
                "Transit",
                "Exfil",
                "Door",
                "Grenmade",
                "Tripwire",
                "Mine",
                "MortarProjectile",
                "BTR"
            };

            foreach (var type in entityTypes)
            {
                if (!Settings.ContainsKey(type))
                    Settings[type] = new EntityTypeSettings();
            }
        }

        /// <summary>
        /// Get settings for a specific entity type, create default if not exists
        /// </summary>
        public EntityTypeSettings GetSettings(string entityType)
        {
            if (!Settings.ContainsKey(entityType))
                Settings[entityType] = new EntityTypeSettings();

            return Settings[entityType];
        }
    }

    /// <summary>
    /// Settings for a specific entity type
    /// </summary>
    public sealed class EntityTypeSettings
    {
        [JsonPropertyName("information")]
        [JsonInclude]
        public HashSet<string> Information { get; set; } = new HashSet<string>
        {
            "Name",
            "Distance",
            "Value"
        };

        [JsonPropertyName("renderDistance")]
        public int RenderDistance { get; set; } = 1500;

        [JsonPropertyName("showImportantLoot")]
        public bool ShowImportantLoot { get; set; } = true;

        [JsonPropertyName("showRadius")]
        public bool ShowRadius { get; set; } = false;
        
        [JsonPropertyName("showLockedDoors")]
        public bool ShowLockedDoors { get; set; } = true;

        [JsonPropertyName("showUnlockedDoors")]
        public bool ShowUnlockedDoors { get; set; } = true;
        
        [JsonPropertyName("hideInactiveExfils")]
        public bool HideInactiveExfils { get; set; } = false;

        [JsonPropertyName("showTripwireLine")]
        public bool ShowTripwireLine { get; set; } = false;

        [JsonIgnore]
        public bool Enabled => RenderDistance > 0;

        [JsonIgnore]
        public bool ShowName => Information.Contains("Name");

        [JsonIgnore]
        public bool ShowDistance => Information.Contains("Distance");

        [JsonIgnore]
        public bool ShowValue => Information.Contains("Value");
    }

    /// <summary>
    /// Configuration for entity type-specific display settings
    /// </summary>
    public sealed class EntityTypeSettingsESPConfig
    {
        /// <summary>
        /// Settings for each entity type
        /// </summary>
        [JsonPropertyName("entityTypeSettingsESP")]
        public Dictionary<string, EntityTypeSettingsESP> Settings { get; set; } = new Dictionary<string, EntityTypeSettingsESP>();

        /// <summary>
        /// Initialize default settings for all entity types
        /// </summary>
        public void InitializeDefaults()
        {
            var entityTypes = new List<string>
            {
                "Airdrop",
                "Corpse",
                "RegularLoot",
                "ImportantLoot",
                "QuestItem",
                "Switch",
                "Transit",
                "Exfil",
                "Door",
                "Grenade",
                "Tripwire",
                "Mine",
                "MortarProjectile",
                "BTR"
            };

            foreach (var type in entityTypes)
            {
                if (!Settings.ContainsKey(type))
                    Settings[type] = new EntityTypeSettingsESP();
            }
        }

        /// <summary>
        /// Get settings for a specific entity type, create default if not exists
        /// </summary>
        public EntityTypeSettingsESP GetSettings(string entityType)
        {
            if (!Settings.ContainsKey(entityType))
                Settings[entityType] = new EntityTypeSettingsESP();

            return Settings[entityType];
        }
    }

    /// <summary>
    /// Settings for a specific entity type
    /// </summary>
    public sealed class EntityTypeSettingsESP
    {
        /// <summary>
        /// Information to display (Name, Distance, Height, Value)
        /// </summary>
        [JsonPropertyName("information")]
        [JsonInclude]
        public HashSet<string> Information { get; set; } = new HashSet<string>
        {
            "Name",
            "Distance",
            "Value"
        };

        /// <summary>
        /// Entity type render distance
        /// </summary>
        [JsonPropertyName("renderDistance")]
        public int RenderDistance { get; set; } = 1500;

        /// <summary>
        /// Grenade trail duration (show last x seconds of trajectory)
        /// </summary>
        [JsonPropertyName("trailDuration")]
        public float TrailDuration { get; set; } = 3f;

        /// <summary>
        /// Min trail distance (minimum distance before adding new trail point)
        /// </summary>
        [JsonPropertyName("minTrailDistance")]
        public float MinTrailDistance { get; set; } = 0.3f;

        /// <summary>
        /// Entity render type (square, circle, diamond etc)
        /// </summary>
        [JsonPropertyName("renderMode")]
        public EntityRenderMode RenderMode { get; set; } = EntityRenderMode.Square;

        [JsonPropertyName("showRadius")]
        public bool ShowRadius { get; set; } = false;

        [JsonPropertyName("grenadeTrail")]
        public bool ShowGrenadeTrail { get; set; } = false;

        [JsonPropertyName("showImportantLoot")]
        public bool ShowImportantLoot { get; set; } = true;

        [JsonPropertyName("showLockedDoors")]
        public bool ShowLockedDoors { get; set; } = true;

        [JsonPropertyName("showUnlockedDoors")]
        public bool ShowUnlockedDoors { get; set; } = true;

        [JsonPropertyName("showTripwireLine")]
        public bool ShowTripwireLine { get; set; } = true;

        [JsonIgnore]
        public bool Enabled => RenderDistance > 0;

        [JsonIgnore]
        public bool ShowName => Information.Contains("Name");

        [JsonIgnore]
        public bool ShowDistance => Information.Contains("Distance");

        [JsonIgnore]
        public bool ShowValue => Information.Contains("Value");
    }

    public sealed class QuestHelperConfig
    {
        /// <summary>
        /// Enables Quest Helper
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Enables only processing kappa related quests
        /// </summary>
        [JsonPropertyName("kappaFilter")]
        public bool KappaFilter { get; set; } = false;

        /// <summary>
        /// Enables processing optional related quest tasks
        /// </summary>
        [JsonPropertyName("optionalTaskFilter")]
        public bool OptionalTaskFilter { get; set; } = false;

        /// <summary>
        /// Enables Quest Kill Zones
        /// </summary>
        [JsonPropertyName("killZones")]
        public bool KillZones { get; set; } = true;

        /// <summary>
        /// Quests that are overridden/disabled.
        /// </summary>
        [JsonPropertyName("blacklistedQuests")]
        [JsonInclude]
        public HashSet<string> BlacklistedQuests { get; set; } = new HashSet<string>();
    }

    public sealed class ContainersConfig
    {
        /// <summary>
        /// Shows static containers on map.
        /// </summary>
        [JsonPropertyName("show")]
        public bool Show { get; set; } = false;

        /// <summary>
        /// Hide containers searched by LocalPlayer.
        /// </summary>
        [JsonPropertyName("hideSearched")]
        public bool HideSearched { get; set; } = false;

        /// <summary>
        /// Selected containers to display.
        /// </summary>
        [JsonPropertyName("selected")]
        public List<string> Selected { get; set; } = new List<string>();
    }

    /// <summary>
    /// Loot Filter Config.
    /// </summary>
    public sealed class LootFilterConfig
    {
        /// <summary> //Mambo
        /// Currently selected filter.
        /// </summary>
        [JsonPropertyName("selected")]
        public string Selected { get; set; } = "default";
        /// <summary>
        /// Filter Entries.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("filters")]
        public Dictionary<string, UserLootFilter> Filters { get; set; } = new()
        {
            ["default"] = new()
        };
    }

    public sealed class ESPConfig
    {
        /// <summary>
        /// Target monitor index for ESP display (0 = primary, 1 = second monitor, etc.)
        /// </summary>
        [JsonPropertyName("espTargetScreen")]
        public int EspTargetScreen { get; set; } = 1; // Default to second monitor (fuser)
        
        /// <summary>
        /// Show FPS Counter in ESP Window.
        /// </summary>
        [JsonPropertyName("showFPS")]
        public bool ShowFPS { get; set; } = true;

        /// <summary>
        /// Enables quest info Widget on fuser.
        /// </summary>
        [JsonPropertyName("showQuestInfoWidget")]
        public bool ShowQuestInfoWidget { get; set; } = true;
        /// <summary>
        /// Enables hotkey info Widget on fuser.
        /// </summary>
        [JsonPropertyName("showHotkeyInfoWidget")]
        public bool ShowHotkeyInfoWidget { get; set; } = true;
        /// <summary>
        /// FPS offset position
        /// </summary>
        [JsonPropertyName("fpsOffset")]
        public PointFSer FPSOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Show Energy & Hydration bar.
        /// </summary>
        [JsonPropertyName("energyHydrationBar")]
        public bool EnergyHydrationBar { get; set; } = true;

        /// <summary>
        /// Status bars (energy/hydration) offset position
        /// </summary>
        [JsonPropertyName("statusBarOffset")]
        public PointFSer StatusBarOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Display Aimline out of the barrel fireport.
        /// </summary>
        [JsonPropertyName("showFireportAim")]
        public bool ShowFireportAim { get; set; } = true;

        /// <summary>
        /// Display Aim Lock of target locked onto via aimbot in ESP.
        /// </summary>
        [JsonPropertyName("showAimLock")]
        public bool ShowAimLock { get; set; } = true;

        /// <summary>
        /// Display Aim FOV in ESP.
        /// </summary>
        [JsonPropertyName("showAimFov")]
        public bool ShowAimFOV { get; set; } = true;

        /// <summary>
        /// Show Magazine / Ammo count in ESP.
        /// </summary>
        [JsonPropertyName("showMagazine")]
        public bool ShowMagazine { get; set; } = true;

        /// <summary>
        /// Magazine counter offset position
        /// </summary>
        [JsonPropertyName("magazineOffset")]
        public PointFSer MagazineOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Show closest player in ESP.
        /// </summary>
        [JsonPropertyName("showClosestPlayer")]
        public bool ShowClosestPlayer { get; set; } = true;

        /// <summary>
        /// Closest player offset position
        /// </summary>
        [JsonPropertyName("closestPlayerOffset")]
        public PointFSer ClosestPlayerOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Show top loot in ESP.
        /// </summary>
        [JsonPropertyName("showTopLoot")]
        public bool ShowTopLoot { get; set; } = true;

        [JsonPropertyName("showKillFeed")]
        public bool ShowKillFeed { get; set; } = true;

        /// <summary>
        /// Closest player offset position
        /// </summary>
        [JsonPropertyName("topLootOffset")]
        public PointFSer TopLootOffset { get; set; } = new PointFSer(0, 0);
        
        [JsonPropertyName("killfeedOffset")]
        public PointFSer KillfeedOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Display Raid Stats (Player Type/Count,etc.) in top right corner.
        /// </summary>
        [JsonPropertyName("showRaidStats")]
        public bool ShowRaidStats { get; set; } = true;

        /// <summary>
        /// Raid stats offset position
        /// </summary>
        [JsonPropertyName("raidStatsOffset")]
        public PointFSer RaidStatsOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// Mini radar configuration options
        /// </summary>
        public ESPMiniRadarOptions MiniRadar { get; set; } = new ESPMiniRadarOptions();

        /// <summary>
        /// Mini radar position and size
        /// </summary>
        [JsonPropertyName("radarRect")]
        public RectFSer RadarRect { get; set; } = new RectFSer(20, 20, 220, 220);

        /// <summary>
        /// Display Status (aimbot enabled, bone, wide lean, etc.) in top center of ESP Screen.
        /// </summary>
        [JsonPropertyName("showStatusText")]
        public bool ShowStatusText { get; set; } = true;

        /// <summary>
        /// Status text offset position
        /// </summary>
        [JsonPropertyName("statusTextOffset")]
        public PointFSer StatusTextOffset { get; set; } = new PointFSer(0, 0);

        /// <summary>
        /// ESP Font Size/Scale.
        /// </summary>
        [JsonPropertyName("fontScale")]
        public float FontScale { get; set; } = 1.0f;

        /// <summary>
        /// ESP Line Thickness/Scale.
        /// </summary>
        [JsonPropertyName("lineScale")]
        public float LineScale { get; set; } = 1.0f;

        /// <summary>
        /// FPS Cap for ESP Rendering.
        /// 0 = Infinite.
        /// </summary>
        [JsonPropertyName("fpsCap")]
        public int FPSCap { get; set; } = 60;

        /// <summary>
        /// Enable 'Auto Full Screen' on Startup/Start.
        /// </summary>
        [JsonPropertyName("autoFS")]
        public bool AutoFullscreen { get; set; } = false;

        /// <summary>
        /// Thew zoom level of the mini radar.
        /// </summary>
        [JsonPropertyName("radarZoom")]
        public float RadarZoom { get; set; } = 4.0f;

        /// <summary>
        /// Selected screen for Auto Startup.
        /// </summary>
        [JsonPropertyName("selectedScreen")]
        public int SelectedScreen { get; set; } = 1;

        /// <summary>
        /// All defined ESP Colors.
        /// </summary>
        [JsonPropertyName("espColors")]
        public Dictionary<EspColorOption, string> Colors { get; set; } = EspColorOptions.GetDefaultColors();

        /// <summary>
        /// Crosshair configuration options
        /// </summary>
        public ESPCrosshairOptions Crosshair { get; set; } = new ESPCrosshairOptions();

        /// <summary>
        /// Player type-specific ESP settings
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("playerTypeESPSettings")]
        public PlayerTypeSettingsESPConfig PlayerTypeESPSettings { get; set; } = new PlayerTypeSettingsESPConfig();

        /// <summary>
        /// Entity type-specific ESP settings
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("entityTypeESPSettings")]
        public EntityTypeSettingsESPConfig EntityTypeESPSettings { get; set; } = new EntityTypeSettingsESPConfig();
    }

    public sealed class ESPMiniRadarOptions
    {
        /// <summary>
        /// Show Mini Radar in ESP.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Show loot on mini radar
        /// </summary>
        [JsonPropertyName("showLoot")]
        public bool ShowLoot { get; set; } = true;

        /// <summary>
        /// Mini radar entity scale
        /// </summary>
        [JsonPropertyName("scale")]
        public float Scale { get; set; } = 1;
    }

    public sealed class ESPCrosshairOptions
    {
        /// <summary>
        /// Show Crosshair in ESP Window.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Crosshair type for ESP Window.
        /// </summary>
        [JsonPropertyName("type")]
        public int Type { get; set; } = 0;

        /// <summary>
        /// Crosshair scale for ESP Window.
        /// </summary>
        [JsonPropertyName("scale")]
        public float Scale { get; set; } = 1;
    }

    public sealed class ESPPlayerRenderOptions
    {
        /// <summary>
        /// Mode to draw in ESP.
        /// </summary>
        [JsonPropertyName("renderingMode")]
        public ESPPlayerRenderMode RenderingMode { get; set; }

        /// <summary>
        /// Show text labels on this player.
        /// </summary>
        [JsonPropertyName("showLabels")]
        public bool ShowLabels { get; set; }

        /// <summary>
        /// Show weapon name on this player.
        /// </summary>
        [JsonPropertyName("showWeapons")]
        public bool ShowWeapons { get; set; }

        /// <summary>
        /// Show distance to this player.
        /// </summary>
        [JsonPropertyName("showDist")]
        public bool ShowDist { get; set; }
    }

    public sealed class MemWritesConfig
    {
        /// <summary>
        /// Keep always off, left for Chams compatibility.
        /// </summary>
        [JsonPropertyName("advancedMemWritesRisky")]
        public bool AdvancedMemWrites { get; set; } = false;        
        /// <summary>
        /// Enables DMA Memory Writing
        /// </summary>
        [JsonPropertyName("enableMemWritesRisky")]
        public bool MemWritesEnabled { get; set; } = false;

        /// <summary>
        /// Enable No Recoil Feature on Startup.
        /// </summary>
        [JsonPropertyName("enableNoRecoil")]
        public bool NoRecoil { get; set; } = false;
        /// </summary>
        [JsonPropertyName("enableNoInertia")]
        public bool NoInertia { get; set; } = false;

        /// <summary>
        /// Amount of 'No Recoil'. 0 = None, 1 = Full
        /// </summary>
        [JsonPropertyName("noRecoilAmount")]
        public int NoRecoilAmount { get; set; } = 0;
        /// <summary>
        /// Enable Extended Reach on Startup.
        /// </summary>
        [JsonPropertyName("extendedReach")]
        public ExtendedReachConfig ExtendedReach { get; set; } = new();
        /// <summary>
        /// Enable Loot Through Walls (LTW) on Startup.
        /// </summary>
        [JsonPropertyName("ltw")]
        public LTWConfig LootThroughWalls { get; set; } = new();        
        /// <summary>
        /// Amount of 'No Sway'. 0 = None, 1 = Full
        /// </summary>
        [JsonPropertyName("noSwayAmount")]
        public int NoSwayAmount { get; set; } = 0;

        /// <summary>
        /// Enable No Visor Feature on Startup.
        /// </summary>
        [JsonPropertyName("noVisor")]
        public bool NoVisor { get; set; } = false;

        [JsonPropertyName("muleMode")]
        public bool MuleMode { get; set; } = false;
        
        [JsonPropertyName("noWeaponMalfunctions")]
        public bool NoWeaponMalfunctions { get; set; } = false;

        [JsonPropertyName("hideRaidCode")]
        public bool HideRaidCode { get; set; } = false;

        [JsonPropertyName("disableFrostbite")]
        public bool DisableFrostbite { get; set; } = false;

        /// <summary>
        /// Enable Disable Grass Feature on Startup.
        /// </summary>
        [JsonPropertyName("disableGrass")]
        public bool DisableGrass { get; set; } = false;

        /// <summary>
        /// Enable MedPanel Feature on Startup.
        /// </summary>
        [JsonPropertyName("medPanel")]
        public bool MedPanel { get; set; } = false;


        [JsonPropertyName("rageMode")]
        public bool RageMode { get; set; } = false;

        /// <summary>
        /// Disable head bobbing
        /// </summary>
        [JsonPropertyName("disableHeadBobbing")]
        public bool DisableHeadBobbing { get; set; } = false;

        /// <summary>
        /// Disable DisableWeaponCollision
        /// </summary>
        [JsonPropertyName("disableWeaponCollision")]
        public bool DisableWeaponCollision { get; set; } = false;

        /// <summary>
        /// Enable Inventory Blur Disable Feature on Startup.
        /// </summary>
        [JsonPropertyName("disableInventoryBlur")]
        public bool DisableInventoryBlur { get; set; } = false;
        /// <summary>
        /// Enable Third Person Feature on Startup.
        /// </summary>
        [JsonPropertyName("thirdPerson")]
        public bool ThirdPerson { get; set; } = false;

        /// <summary>
        /// Enable Clear Weather Feature on Startup.
        /// </summary>
        [JsonPropertyName("clearWeather")]
        public bool ClearWeather { get; set; } = false;

        /// <summary>
        /// Enable Infinite Stamina Feature on Startup.
        /// </summary>
        [JsonPropertyName("infStamina")]
        public bool InfStamina { get; set; } = false;
        /// <summary>
        /// Enable Owl Mode Feature on Startup.
        /// </summary>
        [JsonPropertyName("owlMode")]
        public bool OwlMode { get; set; } = false;

        /// <summary>
        /// Enable No Visor Feature on Startup.
        /// </summary>
        [JsonPropertyName("enableFastDuck")]
        public bool FastDuck { get; set; } = false;

        /// <summary>
        /// Enable Thermal Vision Feature on Startup.
        /// </summary>
        [JsonPropertyName("enableThermalVision")]
        public bool ThermalVision { get; set; } = false;

        /// <summary>
        /// Enable Night Vision Feature on Startup.
        /// </summary>
        [JsonPropertyName("enableNightVision")]
        public bool NightVision { get; set; } = false;

        /// <summary>
        /// Allows instantly planting certain quest items.
        /// </summary>
        [JsonPropertyName("instantPlant")]
        public bool InstantPlant { get; set; } = false;

        /// <summary>
        /// Aimbot Configuration.
        /// </summary>
        [JsonPropertyName("aimbot")]
        public AimbotConfig Aimbot { get; set; } = new();

        /// <summary>
        /// Wide Lean Configuration.
        /// </summary>
        [JsonPropertyName("wideLean")]
        public WideLeanConfig WideLean { get; set; } = new();
        /// <summary>
        /// Chams Feature Config
        /// </summary>
        [JsonPropertyName("chams")]
        public ChamsConfig Chams { get; set; } = new();
        /// <summary>
        /// Makes weapon operations faster (ads, mag loading, etc.)
        /// </summary>
        [JsonPropertyName("fastWeaponOps")]
        public bool FastWeaponOps { get; set; } = false;

        /// <summary>
        /// Makes loading/unloading magazines faster.
        /// </summary>
        [JsonPropertyName("fastLoadUnload")]
        public bool FastLoadUnload { get; set; } = false;

        /// <summary>
        /// Time of Day Configuration.
        /// </summary>
        [JsonPropertyName("timeOfDay")]
        public TimeOfDayConfig TimeOfDay { get; set; } = new();

        /// <summary>
        /// Time of Day Configuration.
        /// </summary>
        [JsonPropertyName("fullBright")]
        public FullBrightConfig FullBright { get; set; } = new();
        /// <summary>
        /// Long Jump Configuration.
        /// </summary>
        [JsonPropertyName("longJump")]
        public LongJumpConfig LongJump { get; set; } = new();

        /// <summary>
        /// Enables big head mode.
        /// </summary>
        [JsonPropertyName("bigHead")]
        public BigHeadConfig BigHead { get; set; } = new();   

        /// <summary>
        /// Move Speed is Enabled.
        /// </summary>
        [JsonPropertyName("moveSpeed")]
        public MoveSpeedConfig MoveSpeed { get; set; } = new();     

    }
    public sealed class MoveSpeedConfig
    {
        /// <summary>
        /// True if move speed is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The multiplier of move speed
        /// </summary>
        [JsonPropertyName("multiplier")]
        public float Multiplier { get; set; } = 1.2f;

        /// <summary>
        /// The multiplier of move speed
        /// </summary>
        [JsonPropertyName("speed")]

        public uint Speed { get; set; } = 0x488;
    }
    public sealed class BigHeadConfig
    {
        /// <summary>
        /// True if Big Heads is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Big head scale
        /// </summary>
        [JsonPropertyName("scale")]
        public float Scale { get; set; } = 1.0f;
    }    
    public sealed class LongJumpConfig
    {
        /// <summary>
        /// True if Long Jump is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Jump multiplier
        /// </summary>
        [JsonPropertyName("multiplier")]
        public float Multiplier { get; set; } = 10f;
    }
    public sealed class FullBrightConfig
    {
        /// <summary>
        /// Enable Full Bright lock.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Intensity .
        /// </summary>
        [JsonPropertyName("intensity")]
        public float Intensity { get; set; } = 12.0f;
    }
    public sealed class TimeOfDayConfig
    {
        /// <summary>
        /// Enable Time of Day lock.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Hour to lock to (0-23).
        /// </summary>
        [JsonPropertyName("hour")]
        public int Hour { get; set; } = 12;
    }

    public sealed class WideLeanConfig
    {
        /// <summary>
        /// Enable Wide Lean Feature on Startup.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Amount of wide lean (scaled to 0.1 - 3.2).
        /// </summary>
        [JsonPropertyName("amount")]
        public float Amount { get; set; } = 0.5f;
    }
    /// <summary>
    /// Loot Through Walls Config.
    /// </summary>
    public sealed class LTWConfig
    {
        /// <summary>
        /// True if LTW is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// LTW Zoom Amount.
        /// </summary>
        [JsonPropertyName("zoomAmount")]
        public float ZoomAmount { get; set; } = 2f;
    }
    public sealed class ExtendedReachConfig
    {
        /// <summary>
        /// True if extended reach is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Extended reach distance.
        /// </summary>
        [JsonPropertyName("distance")]
        public float Distance { get; set; } = 2f;
    }

    public sealed class AimbotConfig
    {
        /// <summary>
        /// Enable Aimbot Feature on Startup.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Last Aimbot Targeting Mode that the player set.
        /// </summary>
        [JsonPropertyName("targetingMode")]
        public Aimbot.AimbotTargetingMode TargetingMode { get; set; } = Aimbot.AimbotTargetingMode.FOV;

        /// <summary>
        /// Aimbot FOV via ESP Circle.
        /// </summary>
        [JsonPropertyName("fov")]
        public float FOV { get; set; } = 150f;

        /// <summary>
        /// Aimbot max aiming distance.
        /// </summary>
        [JsonPropertyName("distance")]
        public float Distance { get; set; } = 500f;

        /// <summary>
        /// Bone for the Default Aimbot Target.
        /// </summary>
        [JsonPropertyName("bone")]
        public Bones Bone { get; set; } = Bones.HumanSpine3;

        /// <summary>
        /// Always headshot AI Targets.
        /// </summary>
        [JsonPropertyName("headshotAI")]
        public bool HeadshotAI { get; set; } = true;

        /// <summary>
        /// True if Aimbot Re-Locking is disabled after a target dies/is no longer valid.
        /// </summary>
        [JsonPropertyName("disableReLock")]
        public bool DisableReLock { get; set; } = false;

        /// <summary>
        /// Silent Aim Config
        /// </summary>
        [JsonPropertyName("silentAimCfg")]
        public SilentAimConfig SilentAim { get; set; } = new();
        /// <summary>
        /// Random Bone Config
        /// </summary>
        [JsonPropertyName("randomBone")]
        public AimbotRandomBoneConfig RandomBone { get; set; } = new();
    }

    public sealed class AimbotRandomBoneConfig
    {
        [JsonIgnore]
        private static readonly Random _rng = new();

        /// <summary>
        /// Enables Random Bone Selection.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;
        /// <summary>
        /// Head shot percentage.
        /// </summary>
        [JsonPropertyName("headPercent")]
        public int HeadPercent { get; set; } = 1;
        /// <summary>
        /// Torso shot percentage.
        /// </summary>
        [JsonPropertyName("torsoPercent")]
        public int TorsoPercent { get; set; } = 33;
        /// <summary>
        /// Arms shot percentage.
        /// </summary>
        [JsonPropertyName("armsPercent")]
        public int ArmsPercent { get; set; } = 33;
        /// <summary>
        /// Legs shot percentage.
        /// </summary>
        [JsonPropertyName("legsPercent")]
        public int LegsPercent { get; set; } = 33;

        /// <summary>
        /// True if all values add up to 100% exactly, otherwise False.
        /// </summary>
        [JsonIgnore]
        public bool Is100Percent => (HeadPercent >= 0 && TorsoPercent >= 0 && ArmsPercent >= 0 && LegsPercent >= 0) &&
            (HeadPercent + TorsoPercent + ArmsPercent + LegsPercent == 100);

        /// <summary>
        /// Reset all values to defaults.
        /// </summary>
        public void ResetDefaults()
        {
            HeadPercent = 1;
            TorsoPercent = 33;
            ArmsPercent = 33;
            LegsPercent = 33;
        }

        /// <summary>
        /// Returns a random bone via the selected percentages.
        /// </summary>
        /// <returns>Skeleton Bone.</returns>
        public Bones GetRandomBone()
        {
            if (!Is100Percent)
                ResetDefaults();
            int roll = _rng.Next(0, 100) + 1;
            if (roll <= HeadPercent)
                return Bones.HumanHead;
            else if (roll <= HeadPercent + TorsoPercent)
                return Random.Shared.GetItems(Skeleton.AllTorsoBones.Span, 1)[0];
            else if (roll <= HeadPercent + TorsoPercent + ArmsPercent)
                return Random.Shared.GetItems(Skeleton.AllArmsBones.Span, 1)[0];
            else // Legs
                return Random.Shared.GetItems(Skeleton.AllLegsBones.Span, 1)[0];
        }
    }

    public sealed class SilentAimConfig
    {
        /// <summary>
        /// Automatically select best target bone.
        /// </summary>
        [JsonPropertyName("autoBone")]
        public bool AutoBone { get; set; } = false;

        /// <summary>
        /// Automatically 'unlock' if target bone leaves FOV.
        /// </summary>
        [JsonPropertyName("safeLock")]
        public bool SafeLock { get; set; } = false;
    }

    /// <summary>
    /// FOV (Field of View) Configuration Settings
    /// </summary>
    public sealed class WidgetsConfig
    {
        #region Aimview

        [JsonInclude]
        [JsonPropertyName("aimviewLocation")]
        public RectFSer _aimviewLoc { get; set; }

        [JsonPropertyName("aimviewMinimized")] public bool AimviewMinimized { get; set; } = false;

        /// <summary>
        /// Aimview Location
        /// </summary>
        [JsonIgnore]
        public SKRect AimviewLocation
        {
            get => new(_aimviewLoc.Left, _aimviewLoc.Top, _aimviewLoc.Right, _aimviewLoc.Bottom);
            set => _aimviewLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion

        #region Player Info

        [JsonInclude]
        [JsonPropertyName("playerInfoLocation")]
        public RectFSer _pInfoLoc { private get; set; }

        [JsonPropertyName("playerInfoMinimized")]
        public bool PlayerInfoMinimized { get; set; } = false;

        /// <summary>
        /// Player Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect PlayerInfoLocation
        {
            get => new(_pInfoLoc.Left, _pInfoLoc.Top, _pInfoLoc.Right, _pInfoLoc.Bottom);
            set => _pInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion

        #region Debug Info

        [JsonInclude]
        [JsonPropertyName("debugInfoLocation")]
        public RectFSer _dInfoLoc { private get; set; }

        [JsonPropertyName("debugInfoMinimized")]
        public bool DebugInfoMinimized { get; set; } = false;

        /// <summary>
        /// Debug Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect DebugInfoLocation
        {
            get => new(_dInfoLoc.Left, _dInfoLoc.Top, _dInfoLoc.Right, _dInfoLoc.Bottom);
            set => _dInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion

        #region Loot Info

        [JsonInclude]
        [JsonPropertyName("lootInfoLocation")]
        public RectFSer _lInfoLoc { private get; set; }

        [JsonPropertyName("lootInfoMinimized")]
        public bool LootInfoMinimized { get; set; } = false;

        /// <summary>
        /// Loot Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect LootInfoLocation
        {
            get => new(_lInfoLoc.Left, _lInfoLoc.Top, _lInfoLoc.Right, _lInfoLoc.Bottom);
            set => _lInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion

        #region Quest Info

        [JsonInclude]
        [JsonPropertyName("questInfoLocation")]
        public RectFSer _qInfoLoc { private get; set; }

        [JsonPropertyName("questInfoMinimized")]
        public bool QuestInfoMinimized { get; set; } = false;

        /// <summary>
        /// Loot Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect QuestInfoLocation
        {
            get => new(_qInfoLoc.Left, _qInfoLoc.Top, _qInfoLoc.Right, _qInfoLoc.Bottom);
            set => _qInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion
    }

    public sealed class ESPWidgetsConfig
    {

        #region Quest Info

        [JsonInclude]
        [JsonPropertyName("questInfoLocationESP")]
        public RectFSer _qInfoLoc { private get; set; }

        [JsonPropertyName("questInfoMinimizedESP")]
        public bool QuestInfoMinimized { get; set; } = false;

        /// <summary>
        /// Loot Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect QuestInfoLocation
        {
            get => new(_qInfoLoc.Left, _qInfoLoc.Top, _qInfoLoc.Right, _qInfoLoc.Bottom);
            set => _qInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion

        #region Hotkey Info

        [JsonInclude]
        [JsonPropertyName("hotkeyInfoLocationESP")]
        public RectFSer _hkInfoLoc { private get; set; }

        [JsonPropertyName("hotkeyInfoMinimizedESP")]
        public bool HotkeyInfoMinimized { get; set; } = false;

        /// <summary>
        /// Loot Info Location
        /// </summary>
        [JsonIgnore]
        public SKRect HotkeyInfoLocation
        {
            get => new(_hkInfoLoc.Left, _hkInfoLoc.Top, _hkInfoLoc.Right, _hkInfoLoc.Bottom);
            set => _hkInfoLoc = new RectFSer(value.Left, value.Top, value.Right, value.Bottom);
        }

        #endregion
    }

    /// <summary>
    /// Configuration for Web Radar.
    /// </summary>
    public sealed class WebRadarConfig
    {
        [JsonPropertyName("webClientUrl")]
        public string WebClientURL { get; set; } = "http://radar.fd-mambo.org";

        [JsonPropertyName("upnp")]
        public bool UPnP { get; set; } = true;

        [JsonPropertyName("host")]
        public string IP { get; set; }

        [JsonPropertyName("port")]
        public string Port { get; set; } = Random.Shared.Next(50000, 60000).ToString();

        [JsonPropertyName("tickRate")]
        public string TickRate { get; set; } = "60";

        [JsonPropertyName("password")]
        public string Password { get; set; }
    }

    /// <summary>
    /// Caches runtime data between sessions.
    /// </summary>
    public sealed class PersistentCache
    {
        [JsonPropertyName("F7xLmP2")]
        [JsonInclude]
        public LowLevelCache LowLevel { get; private set; } = new();

        [JsonPropertyName("profileApi")]
        [JsonInclude]
        public ProfileApiCache ProfileAPI { get; private set; } = new();
    }

    public sealed class ProfileApiCache
    {
        [JsonPropertyName("pid")]
        [JsonInclude]
        public uint PID { get; set; }

        [JsonPropertyName("cache")]
        [JsonInclude]
        public ConcurrentDictionary<string, ProfileData> Profiles { get; private set; } = new();
    }
}