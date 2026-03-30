global using eft_dma_radar;
global using eft_dma_radar.Common;
global using eft_dma_radar.Misc;
global using SDK;
global using SkiaSharp;
global using SkiaSharp.Views.Desktop;
global using System.Buffers;
global using System.Buffers.Binary;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.ComponentModel;
global using System.Diagnostics;
global using System.Net;
global using System.Net.Http.Headers;
global using System.Numerics;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.Hideout;
using eft_dma_radar.Tarkov.QuestPlanner;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.UI;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Windows;
using Application = System.Windows.Forms.Application;
using MessageBox = HandyControl.Controls.MessageBox;

[assembly: AssemblyTitle(Program.Name)]
[assembly: AssemblyProduct(Program.Name)]
[assembly: AssemblyCopyright("BSD Zero Clause License ©2025 lone-dma")]
[assembly: AssemblyDescription("Advanced DMA radar for Escape from Tarkov")]
[assembly: AssemblyCompany("lone-dma")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: SupportedOSPlatform("Windows")]

namespace eft_dma_radar
{
    internal static class Program
    {
        internal const string Name = "EFT DMA Radar";
        internal const string Version = "1.0.0";

        /// <summary>
        /// Current application mode
        /// </summary>
        public static ApplicationMode CurrentMode { get; private set; } = ApplicationMode.Normal;

        /// <summary>
        /// Global Program Configuration.
        /// </summary>
        public static Config Config { get; private set; }

        /// <summary>
        /// Hideout stash manager — reads stash items and calculates sell values.
        /// </summary>
        public static HideoutManager Hideout { get; } = new();

        /// <summary>
        /// Path to the Configuration Folder in %AppData%
        /// </summary>
        public static DirectoryInfo ConfigPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-public"));
        public static DirectoryInfo CustomConfigPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-public", "Configs"));

        /// <summary>
        /// Update the global configuration reference (used for config imports)
        /// </summary>
        /// <param name="newConfig">The new configuration to use</param>
        public static void UpdateConfig(Config newConfig)
        {
            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            Config = newConfig;
            SharedProgram.UpdateConfig(Config);

            Log.WriteLine("[Program] Global config reference updated");
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static public void Main()
        {
            InitializeDpiAwareness();

            var app = new App();
            app.InitializeComponent();

            try
            {
                StartApplication(app, ApplicationMode.Normal);
            }
            catch (Exception ex)
            {
                HandleStartupException(app, ex);
            }
        }

        #region Private Members

        static Program()
        {
            try
            {
                // Ensure both directories exist
                ConfigPath.Create();
                if (!CustomConfigPath.Exists)
                {
                    CustomConfigPath.Create();
                }

                // Check if the 'lastSelectedConfig.json' exists
                var lastSelectedConfigPath = Path.Combine(CustomConfigPath.FullName, "lastSelectedConfig.json");

                string selectedConfigFile = null;
                Config config = null;

                if (File.Exists(lastSelectedConfigPath))
                {
                    // Step 1: Read the last selected config name
                    var lastSelectedConfigJson = File.ReadAllText(lastSelectedConfigPath);
                    var lastSelectedConfigNode = JsonNode.Parse(lastSelectedConfigJson);
                    var configName = lastSelectedConfigNode?.AsObject()?["ConfigFilename"]?.ToString();

                    // Step 2: If the config name exists, look for it in the CustomConfigPath
                    if (!string.IsNullOrWhiteSpace(configName))
                    {
                        var customConfigFiles = Directory.GetFiles(CustomConfigPath.FullName, "*.json");

                        foreach (var file in customConfigFiles)
                        {
                            if (Path.GetFileName(file).Equals(configName, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedConfigFile = Path.GetFileName(file);
                                break;
                            }
                        }
                    }
                }

                // Step 3: If no config found from 'lastSelectedConfig.json', look for default or first config
                if (string.IsNullOrWhiteSpace(selectedConfigFile))
                {
                    var customConfigFiles = Directory.GetFiles(CustomConfigPath.FullName, "*.json");

                    // 3.1 Look for a config marked as default
                    foreach (var file in customConfigFiles)
                    {
                        try
                        {
                            string json = File.ReadAllText(file);
                            var tempConfig = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                ReadCommentHandling = JsonCommentHandling.Skip,
                                AllowTrailingCommas = true
                            });

                            if (tempConfig != null)
                            {
                                selectedConfigFile = Path.GetFileName(file);
                                break;
                            }
                        }
                        catch
                        {
                            // Ignore broken/bad files
                            continue;
                        }
                    }

                    // 3.2 If no default found, use the first config
                    if (string.IsNullOrWhiteSpace(selectedConfigFile) && customConfigFiles.Length > 0)
                    {
                        selectedConfigFile = Path.GetFileName(customConfigFiles[0]);
                    }

                    // 3.3 If no configs in CustomConfigPath, check for legacy config
                    if (string.IsNullOrWhiteSpace(selectedConfigFile))
                    {
                        var legacyConfigPath = Path.Combine(ConfigPath.FullName, "config-eft-v3.json");
                        if (File.Exists(legacyConfigPath))
                        {
                            try
                            {
                                // Copy to CustomConfigPath but don't mark as default
                                var destPath = Path.Combine(CustomConfigPath.FullName, "config-eft-v3.json");
                                File.Copy(legacyConfigPath, destPath);

                                selectedConfigFile = "config-eft-v3.json";
                                Log.WriteLine("[Program] Migrated legacy config-eft-v3.json to custom config directory");
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLine($"[Program] Error migrating legacy config: {ex}");
                            }
                        }
                    }

                    // 3.4 If still no config, create a new one
                    if (string.IsNullOrWhiteSpace(selectedConfigFile))
                    {
                        selectedConfigFile = "config-eft-v3.json";
                        config = new Config
                        {
                            ConfigName = "config-eft-v3",
                            Filename = "config-eft-v3.json"
                        };

                        var newFilePath = Path.Combine(CustomConfigPath.FullName, selectedConfigFile);
                        File.WriteAllText(newFilePath, JsonSerializer.Serialize(config, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        }));

                        Log.WriteLine("[Program] Created new config-eft-v3.json");
                    }
                }

                // Step 4: Load the selected config
                if (config == null)
                {
                    config = Config.Load(selectedConfigFile);
                }

                SharedProgram.Initialize(ConfigPath, config);
                Config = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize configuration: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        /// <summary>
        /// Start the application in the specified mode
        /// </summary>
        private static void StartApplication(App app, ApplicationMode mode)
        {
            CurrentMode = mode;

            var mainWindow = new MainWindow();
            app.MainWindow = mainWindow;

            if (mode == ApplicationMode.Normal)
                ConfigureProgram();
            else
                ConfigureSafeMode();

            mainWindow.Show();
            mainWindow.Activate();

            app.Run();
        }

        /// <summary>
        /// Handle startup exceptions and offer recovery options
        /// </summary>
        private static void HandleStartupException(App app, Exception ex)
        {
            var errorMessage = ex.ToString();

            if (errorMessage.Contains("DMA Initialization Failed!"))
            {
                errorMessage += "\n\nWould you like to continue in Safe Mode? (UI and Config only)";

                var result = MessageBox.Show(errorMessage, "Continue in Safe Mode?", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    Environment.Exit(1);
            }
            else
            {
                var result = MessageBox.Show(
                    $"Startup Error: {ex.Message}\n\nWould you like to start in Safe Mode?",
                    "Startup Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                    StartApplication(app, ApplicationMode.SafeMode);
                else
                    throw new Exception($"Application startup failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Configure the program for safe mode (no DMA functionality)
        /// </summary>
        private static void ConfigureSafeMode()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Starting in Safe Mode...", 10);
                Log.WriteLine("Starting application in Safe Mode - DMA functionality disabled");

                loading.UpdateStatus("Loading Configuration...", 25);

                loading.UpdateStatus("Initializing Safe Memory Interface...", 40);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Safe UI Components...", 50);
                try
                {
                    loading.UpdateStatus("Loading Tarkov.Dev Data...", 60);
                    EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                    loading.UpdateStatus("Caching Item Icons...", 70);
                    CacheAllItemIcons();
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"Non-critical safe mode component failed: {ex.Message}");
                }

                loading.UpdateStatus("Initializing Safe Mode Features...", 85);
                loading.UpdateStatus("Safe Mode Ready - DMA functions disabled", 100);
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Safe Mode initialization failed: {ex}");
                loading.UpdateStatus("Safe Mode initialization failed", 100);
                Thread.Sleep(1000);
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        /// <summary>
        /// Configure Program Startup (Normal Mode).
        /// </summary>
        private static void ConfigureProgram()
        {
            var loading = LoadingWindow.Create();

            try
            {
                loading.UpdateStatus("Loading Tarkov.Dev Data...", 15);
                EftDataManager.ModuleInitAsync(loading).GetAwaiter().GetResult();

                loading.UpdateStatus("Caching Item Icons...", 25);
                CacheAllItemIcons();

                loading.UpdateStatus("Loading Map Assets...", 35);
                XMMapManager.ModuleInit();

                loading.UpdateStatus("Starting DMA Connection...", 50);
                MemoryInterface.ModuleInit();

                loading.UpdateStatus("Loading Remaining Modules...", 75);
                FeatureManager.ModuleInit();
                QuestPlannerWorker.ModuleInit(); // Quest Planner background service

                ResourceJanitor.ModuleInit(new Action(CleanupWindowResources));

                loading.UpdateStatus("Loading Completed!", 100);
                Thread.Sleep(300);
            }
            finally
            {
                loading.Dispatcher.Invoke(() => loading.Close());
            }
        }

        private static void CacheAllItemIcons()
        {
            string iconCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar-public", "Assets", "Icons", "Items");

            Directory.CreateDirectory(iconCachePath);

            var itemsToCache = EftDataManager.AllItems.Keys
                .Where(itemId =>
                {
                    string pngPath = Path.Combine(iconCachePath, $"{itemId}.png");
                    return !File.Exists(pngPath) || new FileInfo(pngPath).Length <= 1024;
                })
                .ToList();

            if (itemsToCache.Count == 0)
                return;

            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };

            Parallel.ForEachAsync(itemsToCache, options, async (itemId, ct) =>
            {
                try
                {
                    // Use debug level for icon caching - only visible when debug logging is enabled
                    Log.Write(AppLogLevel.Debug, $"Caching item icon: {itemId}", "IconCache");
                    await Converters.ItemIconConverter.SaveItemIconAsPng(itemId, iconCachePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[IconCache] Failed to cache item {itemId}: {ex}");
                }
            }).GetAwaiter().GetResult();
        }

        private static void CleanupWindowResources()
        {
            MainWindow.Window?.PurgeSKResources();
            ESPForm.Window?.PurgeSKResources();
        }

        /// <summary>
        /// Initialize DPI awareness for the application
        /// </summary>
        private static void InitializeDpiAwareness()
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

                Log.WriteLine("[DPI] Successfully enabled PerMonitorV2 DPI awareness");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DPI] Failed to set DPI awareness: {ex.Message}");

                try
                {
                    Application.SetHighDpiMode(HighDpiMode.SystemAware);
                    Log.WriteLine("[DPI] Fallback: Enabled SystemAware DPI awareness");
                }
                catch
                {
                    Log.WriteLine("[DPI] Warning: Could not enable DPI awareness");
                }
            }
        }

        #endregion
    }
}