using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using System.Net.Http;
using System.Windows.Media.Imaging;
using SkiaSharp;
using System.IO;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.WebRadar;
using eft_dma_radar.UI.ESP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.SKWidgetControl;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Maps;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Config;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Misc.Data.EFT;
using eft_dma_radar.UI.Controls;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.LowLevel;
using eft_dma_radar.Common.Unity.LowLevel.PhysX;
using HandyControl.Controls;
using HandyControl.Data;
using HandyControl.Themes;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using static eft_dma_radar.Tarkov.Features.MemoryWrites.Aimbot;
using static SDK.Offsets;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using InputManager = eft_dma_radar.Common.Misc.InputManager;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for GeneralSettingsControl.xaml
    /// </summary>
    public partial class GeneralSettingsControl : UserControl
    {
        #region Fields and Properties
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        public ObservableCollection<QuestListItem> QuestItems { get; } = new();

        public ObservableCollection<HotkeyActionModel> AvailableHotkeyActions { get; } = new();
        private readonly Dictionary<string, List<int>> _actionKeyMappings = new();
        private readonly Dictionary<int, string> _actionIdToKeyMap = new();
        private ObservableCollection<HotkeyDisplayModel> _hotkeyList = new();
        private readonly Dictionary<string, bool> _toggleStates = new();
        private readonly Dictionary<string, DateTime> _lastExecutionTime = new();
        private const int HOTKEY_COOLDOWN_MS = 50; // Prevent spam
        private bool _keyInputBoxIsCapturing = false;

        private const int INTERVAL = 100; // 0.1 second
        private const int HK_ZoomAmt = 2; // amt to zoom

        private bool _uiReady = false;
        private bool _suppressApiEvents = false;

        private PopupWindow _openColorPicker;
        private Dictionary<string, SolidColorBrush> _brushFields = new Dictionary<string, SolidColorBrush>();

        private static Config Config => Program.Config;

        private string _currentPlayerType;
        private string _currentEntityType;
        private bool _isLoadingPlayerSettings = false;
        private bool _isLoadingSettingAndWidgets = false;
        private bool _isLoadingEntitySettings = false;

        private MainWindow mainWindow => MainWindow.Window;

        private readonly string[] _availableInformation = new string[]
        {
            "ADS",
            "Ammo Type",
            "Distance",
            "Group",
            "Health",
            "Height",
            "Level",
            "Name",
            "Night Vision",
            "KD",
            "Tag",
            "Thermal",
            "UBGL",
            "Value",
            "Weapon"
        };

        private readonly string[] _availableWidgets = new string[]
        {
            "Aimview Widget",
            "Debug Widget",
            "Player Info Widget",
            "Loot Info Widget",
            "Quest Info Widget",
            "HotKey Info Widget"
        };

        private readonly string[] _availableGeneralOptions = new string[]
        {
            "Connect Groups",
            "Mask Names",
            "Players on Top"
        };

        private readonly string[] _availableEntityInformation = new string[]
        {
            "Name",
            "Distance",
            "Value"
        };
        #endregion

        public GeneralSettingsControl()
        {
            InitializeComponent();
            TooltipManager.AssignGeneralSettingsTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("GeneralSettings");
                ExpanderManager.Instance.RegisterExpanders(this, "GeneralSettings",
                    expGeneralOptions,
                    expPlayerInformation,
                    expEntityInformation,
                    expMonitorSettings,
                    expQuestHelper,
                    expWebRadar,
                    expPlayerAPIService,
                    expPlayerColors,
                    expLootColors,
                    expOtherColors,
                    expHUDColors,
                    expInterfaceColors,
                    expApplicationHotkeys);

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    this.DataContext = this;

                    InitializeControlEvents();
                    LoadSettings();
                    InitializeConfigTab();
                }
                catch (TimeoutException ex)
                {
                    XMLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        #region General Settings Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterGeneralEvents();
                RegisterColorEvents();
                RegisterHotkeyEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;

            btnMenu.Click += GeneralButton_Click;
            mnuExportConfig.Click += GeneralMenuItem_Click;
            mnuImportConfig.Click += GeneralMenuItem_Click;
        }

        private void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                LoadGeneralSettings();
                LoadColorSettings();
                LoadHotkeySettings();
                LoadApiSettings();
                _uiReady = true;  // mark UI ready only after all programmatic changes done
            });
        }
        private void OpenContextMenu()
        {
            var btn = btnMenu;

            if (btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private async void ImportConfigFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    NotificationsShared.Warning("[Config] Clipboard does not contain text data.");
                    return;
                }

                var clipboardText = Clipboard.GetText();



                var warningResult = MessageBox.Show(
                        "WARNING: Importing a configuration will replace current settings including:\n\n" +
                        "ï¿? General settings & UI preferences\n" +
                        "ï¿? Player/Entity display settings\n" +
                        "ï¿? Color configurations\n" +
                        "ï¿? Hotkey assignments\n" +
                        "ï¿? ESP configurations\n" +
                        "ï¿? Panel and toolbar positions\n" +
                        "ï¿? Memory writing settings\n" +
                        "ï¿? Loot settings\n" +
                        "ï¿? Quest helper settings\n" +
                        "ï¿? Container settings\n\n" +
                        "NOTE: Cache & Web Radar data will not be preserved.\n\n" +
                        "This action cannot be undone. Continue?",
                        "Import Configuration Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (warningResult != MessageBoxResult.Yes)
                    return;

                var importButton = this.FindName("mnuImportConfig") as MenuItem;
                if (importButton != null)
                    importButton.IsEnabled = false;

                try
                {
                    Config importedConfig = null;

                    await Task.Run(() =>
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

                            importedConfig = JsonSerializer.Deserialize<Config>(clipboardText, options);

                            if (importedConfig == null)
                            {
                                throw new InvalidOperationException("Deserialized config is null");
                            }

                            XMLogging.WriteLine("[Config] Configuration deserialized successfully");
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[Config] Failed to process configuration: {ex.Message}");
                            throw new JsonException("Invalid configuration data in clipboard", ex);
                        }
                    });

                    if (importedConfig == null)
                    {
                        NotificationsShared.Error("[Config] Invalid configuration data in clipboard.");
                        return;
                    }

                    NotificationsShared.Info("[Config] Applying imported configuration...");

                    await Task.Run(async () =>
                    {
                        try
                        {
                            XMLogging.WriteLine("[Config] Starting config import process...");

                            var currentCache = Config.Cache;
                            var currentWebRadar = Config.WebRadar;

                            Config.EnsureComplexObjectsInitialized(importedConfig);

                            importedConfig.Cache = currentCache;
                            importedConfig.WebRadar = currentWebRadar;

                            if (importedConfig.MemWrites.MemWritesEnabled)
                            {
                                var memoryWritingDecision = MemoryWritingControl.HandleConfigImportMemoryWriting(importedConfig);
                                MemoryWritingControl.MemoryWritingImportHandler.ApplyMemoryWritingDecision(importedConfig, memoryWritingDecision);
                            }

                            Program.UpdateConfig(importedConfig);

                            await Dispatcher.InvokeAsync(() =>
                            {
                                var mainWindow = MainWindow.Window;
                                if (mainWindow != null)
                                {
                                    mainWindow.ValidateAndFixImportedPanelPositions();
                                    mainWindow.ValidateAndFixImportedToolbarPosition();
                                }
                            });

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                LoadGeneralSettings();
                                await Task.Delay(50);

                                UpdateUIScale();
                                await Task.Delay(50);

                                LoadColorSettings();
                                await Task.Delay(50);

                                LoadHotkeySettings();
                                await Task.Delay(50);
                            });

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                var mainWindow = MainWindow.Window;
                                if (mainWindow != null)
                                {
                                    if (mainWindow.MemoryWritingControl != null)
                                    {
                                        MemWrites.Enabled = Config.MemWrites.MemWritesEnabled;
                                        mainWindow.MemoryWritingControl.LoadSettings();
                                        await Task.Delay(50);

                                        mainWindow.MemoryWritingControl.FeatureInstanceCheck();
                                        await Task.Delay(50);
                                    }

                                    if (mainWindow.LootSettingsControl != null)
                                    {
                                        mainWindow.LootSettingsControl.LoadSettings();
                                        await Task.Delay(50);

                                        await Task.Run(() => RefreshContainerData());
                                        await Task.Delay(50);
                                    }

                                    if (mainWindow.ESPControl != null)
                                    {
                                        mainWindow.ESPControl.LoadSettings();
                                        await Task.Delay(50);
                                    }

                                    mainWindow.RestorePanelPositions();
                                    mainWindow.RestoreToolbarPosition();

                                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                                    timer.Tick += (s, args) =>
                                    {
                                        timer.Stop();
                                        mainWindow.EnsureAllPanelsInBounds();
                                        if (mainWindow.customToolbar != null)
                                            mainWindow.EnsurePanelInBounds(mainWindow.customToolbar, mainWindow.mainContentGrid);

                                        XMLogging.WriteLine("[Config] Panel and toolbar positions applied and validated");
                                    };
                                    timer.Start();
                                }
                            });

                            await Task.Run(() => RefreshQuestData());
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateFeatureInstances();
                            });

                            Config.Save();

                            XMLogging.WriteLine("[Config] Configuration imported successfully");
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[Config] Import error during config application: {ex}");
                            throw;
                        }
                    });

                    NotificationsShared.Success("Configuration imported successfully!");
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Config] Import error: {ex}");
                    NotificationsShared.Error($"[Config] Import error: {ex.Message}");
                }
                finally
                {
                    if (importButton != null)
                        importButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Import error: {ex}");
                NotificationsShared.Error($"[Config] Import error: {ex.Message}");
            }
        }

        private void RefreshContainerData()
        {
            try
            {
                var mainWindow = MainWindow.Window;
                if (mainWindow?.LootSettingsControl != null)
                {
                    var refreshMethod = mainWindow.LootSettingsControl.GetType()
                        .GetMethod("RefreshContainerData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    refreshMethod?.Invoke(mainWindow.LootSettingsControl, null);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Error refreshing container data: {ex}");
            }
        }

        private void RefreshQuestData()
        {
            try
            {
                if (Config.QuestHelper.Enabled)
                    RefreshQuestHelper();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Error refreshing quest data: {ex}");
            }
        }

        private void UpdateFeatureInstances()
        {
            try
            {
                var mainWindow = MainWindow.Window;
                MemWrites.Enabled = Config.MemWrites.MemWritesEnabled;

                if (mainWindow?.MemoryWritingControl != null)
                    mainWindow.MemoryWritingControl.FeatureInstanceCheck();

                XMLogging.WriteLine("[Config] Feature instances updated successfully");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Error updating feature instances: {ex}");
            }
        }
        #endregion

        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
            _openColorPicker?.Close();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BringToFrontRequested?.Invoke(this, EventArgs.Empty);

            DragHandle.CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            DragHandle.MouseMove += DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
        }

        private void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var offset = currentPosition - _dragStartPoint;

                DragRequested?.Invoke(this, new PanelDragEventArgs(offset.X, offset.Y));
            }
        }

        private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            DragHandle.ReleaseMouseCapture();
            DragHandle.MouseMove -= DragHandle_MouseMove;
            DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).CaptureMouse();
            _dragStartPoint = e.GetPosition(this);

            ((UIElement)sender).MouseMove += ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
        }

        private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPosition = e.GetPosition(this);
                var sizeDelta = currentPosition - _dragStartPoint;

                ResizeRequested?.Invoke(this, new PanelResizeEventArgs(sizeDelta.X, sizeDelta.Y));
                _dragStartPoint = currentPosition;
            }
        }

        private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ((UIElement)sender).ReleaseMouseCapture();
            ((UIElement)sender).MouseMove -= ResizeHandle_MouseMove;
            ((UIElement)sender).MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;
        }

        private void GeneralButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "ContextMenu":
                        OpenContextMenu();
                        break;
                }
            }
        }

        private void GeneralMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu && mnu.Tag is string tag)
            {
                switch (tag)
                {
                    case "ExportConfig":
                        ExportConfigToClipboard();
                        break;
                    case "ImportConfig":
                        ImportConfigFromClipboard();
                        break;
                }
            }
        }
        #endregion
        #endregion

        #region General Tab
        #region Functions/Methods
        private void RegisterGeneralEvents()
        {
            // General Options
            chkMapSetup.Checked += GeneralCheckbox_Checked;
            chkMapSetup.Unchecked += GeneralCheckbox_Checked;
            ccbWidgets.SelectionChanged += widgetsCheckComboBox_SelectionChanged;
            ccbGeneralOptions.SelectionChanged += generalOptionsCheckComboBox_SelectionChanged;

            btnSendStashDogTags.Click += btnSendStashDogTags_Click;
            nudFPSLimit.ValueChanged += GeneralNUD_ValueChanged;
            sldrUIScale.ValueChanged += GeneralSlider_ValueChanged;

            // Player Information
            cboPlayerType.SelectionChanged += cboPlayerType_SelectionChanged;
            chkHeightIndicator.Checked += GeneralCheckbox_Checked;
            chkHeightIndicator.Unchecked += GeneralCheckbox_Checked;
            chkImportantIndicator.Checked += GeneralCheckbox_Checked;
            chkImportantIndicator.Unchecked += GeneralCheckbox_Checked;
            chkHighAlert.Checked += GeneralCheckbox_Checked;
            chkHighAlert.Unchecked += GeneralCheckbox_Checked;
            chkShowImportantPlayerLoot.Checked += GeneralCheckbox_Checked;
            chkShowImportantPlayerLoot.Unchecked += GeneralCheckbox_Checked;
            sldrPlayerTypeRenderDistance.ValueChanged += GeneralSlider_ValueChanged;
            sldrPlayerTypeAimlineLength.ValueChanged += GeneralSlider_ValueChanged;
            ccbInformation.SelectionChanged += playerInfoCheckComboBox_SelectionChanged;
            sldrMinimumKD.ValueChanged += GeneralSlider_ValueChanged;

            // Entity Information
            cboEntityType.SelectionChanged += cboEntityType_SelectionChanged;
            sldrEntityTypeRenderDistance.ValueChanged += GeneralSlider_ValueChanged;
            ccbEntityInformation.SelectionChanged += entityInfoCheckComboBox_SelectionChanged;
            chkShowImportantCorpseLoot.Checked += GeneralCheckbox_Checked;
            chkShowImportantCorpseLoot.Unchecked += GeneralCheckbox_Checked;
            chkExplosiveRadius.Checked += GeneralCheckbox_Checked;
            chkExplosiveRadius.Unchecked += GeneralCheckbox_Checked;
            chkShowLockedDoors.Checked += GeneralCheckbox_Checked;
            chkShowLockedDoors.Unchecked += GeneralCheckbox_Checked;
            chkShowUnlockedDoors.Checked += GeneralCheckbox_Checked;
            chkShowUnlockedDoors.Unchecked += GeneralCheckbox_Checked;
            chkShowTripwireLine.Checked += GeneralCheckbox_Checked;
            chkShowTripwireLine.Unchecked += GeneralCheckbox_Checked;
            chkHideInactive.Checked += GeneralCheckbox_Checked;
            chkHideInactive.Unchecked += GeneralCheckbox_Checked;

            // Monitor
            cboMonitor.SelectionChanged += GeneralComboBox_SelectionChanged;
            btnRefreshMonitors.Click += btnRefreshMonitors_Click;
            txtGameWidth.TextChanged += GeneralTextbox_TextChanged;
            txtGameHeight.TextChanged += GeneralTextbox_TextChanged;

            // Quest Helper
            chkQuestHelper.Checked += GeneralCheckbox_Checked;
            chkQuestHelper.Unchecked += GeneralCheckbox_Checked;
            chkQuestsSelectAll.Checked += GeneralCheckbox_Checked;
            chkQuestsSelectAll.Unchecked += GeneralCheckbox_Checked;
            chkKappaFilter.Checked += GeneralCheckbox_Checked;
            chkKappaFilter.Unchecked += GeneralCheckbox_Checked;
            chkOptionalTaskFilter.Checked += GeneralCheckbox_Checked;
            chkOptionalTaskFilter.Unchecked += GeneralCheckbox_Checked;
            chkKillZones.Checked += GeneralCheckbox_Checked;
            chkKillZones.Unchecked += GeneralCheckbox_Checked;

            // Web Radar Server
            btnWebRadarStart.Click += btnWebRadarStart_Click;
            chkWebRadarUPnP.Checked += GeneralCheckbox_Checked;
            chkWebRadarUPnP.Unchecked += GeneralCheckbox_Checked;
            lblWebRadarLink.MouseLeftButtonUp += lblWebRadarLink_MouseLeftButtonUp;
            txtWebRadarPort.TextChanged += GeneralTextbox_TextChanged;


            // Player API Service
            rdbTarkovDev.Checked += GeneralRadioButton_Checked;
            rdbEftApiTech.Checked += GeneralRadioButton_Checked;
            btnCreateApiFile.Click += btnCreateApiFile_Click;
            btnOpenApiFolder.Click += btnOpenApiFolder_Click;
            btnClearApiFile.Click += btnClearApiFile_Click;
        }

        private void LoadGeneralSettings()
        {
            // General Options
            LoadGeneralOptions();

            nudFPSLimit.Value = Config.RadarTargetFPS;
            sldrUIScale.Value = Config.UIScale;

            // Monitor
            txtGameHeight.Text = Config.MonitorHeight.ToString();
            txtGameWidth.Text = Config.MonitorWidth.ToString();
            CameraManagerBase.UpdateViewportRes();

            // Quest Helper
            chkQuestHelper.IsChecked = Config.QuestHelper.Enabled;
            chkKappaFilter.IsChecked = Config.QuestHelper.KappaFilter;
            chkOptionalTaskFilter.IsChecked = Config.QuestHelper.OptionalTaskFilter;
            chkKillZones.IsChecked = Config.QuestHelper.KillZones;
            RefreshQuestHelper();

            // Web Radar Server
            InitializeWebRadar();

            // Player API Service
            var alternateService = Config.AlternateProfileService;
            if (alternateService)
                rdbEftApiTech.IsChecked = true;
            else
                rdbTarkovDev.IsChecked = true;

            UpdateUIScale();

            InitializePlayerTypeSettings();
            InitializeEntityTypeSettings();
        }

        private void InitializePlayerTypeSettings()
        {
            if (Config.PlayerTypeSettings == null)
                Config.PlayerTypeSettings = new PlayerTypeSettingsConfig();

            Config.PlayerTypeSettings.InitializeDefaults();
            Config.Save();
            cboPlayerType.Items.Clear();

            var playerTypeItems = new List<ComboBoxItem>();

            foreach (PlayerType type in Enum.GetValues(typeof(PlayerType)))
            {
                if (type != PlayerType.Default)
                {
                    var displayName = type == PlayerType.AIRaider ?
                        "Raider/Rogue/Guard" :
                        type.GetDescription();
                    var item = new ComboBoxItem
                    {
                        Content = displayName,
                        Tag = type.ToString()
                    };
                    playerTypeItems.Add(item);
                }
            }

            playerTypeItems.Add(new ComboBoxItem { Content = "Aimbot Locked", Tag = "AimbotLocked" });
            playerTypeItems.Add(new ComboBoxItem { Content = "Focused", Tag = "Focused" });
            playerTypeItems.Add(new ComboBoxItem { Content = "LocalPlayer", Tag = "LocalPlayer" });
            playerTypeItems.Sort((x, y) => string.Compare(x.Content.ToString(), y.Content.ToString()));

            foreach (var item in playerTypeItems)
            {
                cboPlayerType.Items.Add(item);
            }

            ccbInformation.Items.Clear();

            foreach (var info in _availableInformation)
            {
                ccbInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboPlayerType.Items.Count > 0)
            {
                cboPlayerType.SelectedIndex = 0;
                _currentPlayerType = ((ComboBoxItem)cboPlayerType.SelectedItem).Tag.ToString();
                LoadPlayerTypeSettings(_currentPlayerType);
            }
        }

        private void InitializeEntityTypeSettings()
        {
            if (Config.EntityTypeSettings == null)
                Config.EntityTypeSettings = new EntityTypeSettingsConfig();

            Config.EntityTypeSettings.InitializeDefaults();
            Config.Save();
            cboEntityType.Items.Clear();

            var entityTypeItems = new List<ComboBoxItem>
            {
                new ComboBoxItem { Content = "Static Container", Tag = "StaticContainer" },
                new ComboBoxItem { Content = "Corpse", Tag = "Corpse" },
                new ComboBoxItem { Content = "Regular Loot", Tag = "RegularLoot" },
                new ComboBoxItem { Content = "Important Loot", Tag = "ImportantLoot" },
                new ComboBoxItem { Content = "Quest Item", Tag = "QuestItem" },
                new ComboBoxItem { Content = "Quest Zone", Tag = "QuestZone" },
                new ComboBoxItem { Content = "Switch", Tag = "Switch" },
                new ComboBoxItem { Content = "Transit", Tag = "Transit" },
                new ComboBoxItem { Content = "Exfil", Tag = "Exfil" },
                new ComboBoxItem { Content = "Door", Tag = "Door" },
                new ComboBoxItem { Content = "Grenade", Tag = "Grenade" },
                new ComboBoxItem { Content = "Tripwire", Tag = "Tripwire" },
                new ComboBoxItem { Content = "Mine", Tag = "Mine" },
                new ComboBoxItem { Content = "Mortar Projectile", Tag = "MortarProjectile" },
                new ComboBoxItem { Content = "Airdrop", Tag = "Airdrop" },
                new ComboBoxItem { Content = "BTR", Tag = "BTR" }
            };

            entityTypeItems.Sort((x, y) => string.Compare(x.Content.ToString(), y.Content.ToString()));

            foreach (var item in entityTypeItems)
            {
                cboEntityType.Items.Add(item);
            }

            ccbEntityInformation.Items.Clear();

            foreach (var info in _availableEntityInformation)
            {
                ccbEntityInformation.Items.Add(new CheckComboBoxItem { Content = info });
            }

            if (cboEntityType.Items.Count > 0)
            {
                cboEntityType.SelectedIndex = 0;
                _currentEntityType = ((ComboBoxItem)cboEntityType.SelectedItem).Tag.ToString();
                LoadEntityTypeSettings(_currentEntityType);
            }
        }

        private void LoadGeneralOptions()
        {
            _isLoadingSettingAndWidgets = true;

            try
            {
                ccbWidgets.Items.Clear();
                foreach (var widget in _availableWidgets)
                {
                    ccbWidgets.Items.Add(new CheckComboBoxItem { Content = widget });
                }

                ccbGeneralOptions.Items.Clear();
                foreach (var option in _availableGeneralOptions)
                {
                    ccbGeneralOptions.Items.Add(new CheckComboBoxItem { Content = option });
                }
            }
            finally
            {
                _isLoadingSettingAndWidgets = false;
            }

            UpdateWidgetOptionSelections();
            UpdateGeneralOptionSelections();
        }

        private void UpdateWidgetOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Aimview Widget"] = Config.AimviewWidgetEnabled,
                ["Debug Widget"] = Config.ShowDebugWidget,
                ["Player Info Widget"] = Config.ShowInfoTab,
                ["Loot Info Widget"] = Config.ShowLootInfoWidget,
                ["Quest Info Widget"] = Config.ShowQuestInfoWidget,
                ["HotKey Info Widget"] = Config.ESP.ShowHotkeyInfoWidget
            };

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void UpdateGeneralOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                //["Connect Groups"] = Config.ConnectGroups,
                ["Mask Names"] = Config.MaskNames,
                ["Players on Top"] = Config.PlayersOnTop
            };

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void UpdateSpecificWidgetOption(string widgetName, bool isSelected)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                if (item.Content.ToString() == widgetName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            XMLogging.WriteLine($"Updated widget option: {widgetName} = {isSelected}");
        }

        private void UpdateSpecificGeneralOption(string optionName, bool isSelected)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            XMLogging.WriteLine($"Updated general option: {optionName} = {isSelected}");
        }

        private void LoadPlayerTypeSettings(string playerType)
        {
            _isLoadingPlayerSettings = true;
            try
            {
                var settings = Config.PlayerTypeSettings.GetSettings(playerType);

                chkHeightIndicator.IsChecked = settings.HeightIndicator;
                chkImportantIndicator.IsChecked = settings.ImportantIndicator;
                chkHighAlert.IsChecked = settings.HighAlert;
                chkShowImportantPlayerLoot.IsChecked = settings.ShowImportantLoot;
                sldrPlayerTypeRenderDistance.Value = settings.RenderDistance;
                sldrPlayerTypeAimlineLength.Value = settings.AimlineLength;
                sldrMinimumKD.Value = settings.MinKD;

                ccbInformation.SelectedItems.Clear();

                foreach (CheckComboBoxItem item in ccbInformation.Items)
                {
                    var info = item.Content.ToString();

                    if (settings.Information.Contains(info))
                        item.IsSelected = true;
                    else
                        item.IsSelected = false;
                }
            }
            finally
            {
                _isLoadingPlayerSettings = false;
            }

            UpdatePlayerInformationControlsVisibility();
        }

        private void UpdatePlayerInformationControlsVisibility()
        {
            if (_isLoadingPlayerSettings)
                return;

            kdSettings.Visibility = Visibility.Collapsed;

            var showKD = false;
            foreach (CheckComboBoxItem item in ccbInformation.SelectedItems)
            {
                var info = item.Content.ToString();
                if (info == "KD")
                {
                    showKD = true;
                    break;
                }
            }

            if (showKD)
                kdSettings.Visibility = Visibility.Visible;
        }

        private void SavePlayerTypeSettings(string playerType)
        {
            if (_isLoadingPlayerSettings)
                return;

            var settings = Config.PlayerTypeSettings.GetSettings(playerType);
            settings.HeightIndicator = chkHeightIndicator.IsChecked == true;
            settings.ImportantIndicator = chkImportantIndicator.IsChecked == true;
            settings.HighAlert = chkHighAlert.IsChecked == true;
            settings.ShowImportantLoot = chkShowImportantPlayerLoot.IsChecked == true;
            settings.RenderDistance = (int)sldrPlayerTypeRenderDistance.Value;
            settings.AimlineLength = (int)sldrPlayerTypeAimlineLength.Value;
            settings.MinKD = (float)sldrMinimumKD.Value;
            settings.Information.Clear();

            foreach (CheckComboBoxItem item in ccbInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            Config.Save();
            XMLogging.WriteLine($"Saved player type settings for {playerType}");
        }

        private void LoadEntityTypeSettings(string entityType)
        {
            _isLoadingEntitySettings = true;
            try
            {
                var settings = Config.EntityTypeSettings.GetSettings(entityType);

                sldrEntityTypeRenderDistance.Value = settings.RenderDistance;

                ccbEntityInformation.SelectedItems.Clear();

                foreach (CheckComboBoxItem item in ccbEntityInformation.Items)
                {
                    var info = item.Content.ToString();

                    if (settings.Information.Contains(info))
                        item.IsSelected = true;
                    else
                        item.IsSelected = false;
                }

                switch (entityType)
                {
                    case "Corpse":
                        chkShowImportantCorpseLoot.IsChecked = settings.ShowImportantLoot;
                        break;
                    case "Grenade":
                        chkExplosiveRadius.IsChecked = settings.ShowRadius;
                        break;
                    case "Door":
                        chkShowLockedDoors.IsChecked = settings.ShowLockedDoors;
                        chkShowUnlockedDoors.IsChecked = settings.ShowUnlockedDoors;
                        break;
                    case "Exfil":
                        chkHideInactive.IsChecked = settings.HideInactiveExfils;
                        break;
                    case "Tripwire":
                        chkShowTripwireLine.IsChecked = settings.ShowTripwireLine;
                        break;
                }
            }
            finally
            {
                _isLoadingEntitySettings = false;
            }

            corpseSettings.Visibility = Visibility.Collapsed;
            grenadeSettings.Visibility = Visibility.Collapsed;
            doorSettings.Visibility = Visibility.Collapsed;
            exfilSettings.Visibility = Visibility.Collapsed;
            tripwireSettings.Visibility = Visibility.Collapsed;

            switch (_currentEntityType)
            {
                case "Corpse":
                    corpseSettings.Visibility = Visibility.Visible;
                    break;
                case "Grenade":
                    grenadeSettings.Visibility = Visibility.Visible;
                    break;
                case "Door":
                    doorSettings.Visibility = Visibility.Visible;
                    break;
                case "Exfil":
                    exfilSettings.Visibility = Visibility.Visible;
                    break;
                case "Tripwire":
                    tripwireSettings.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void SaveEntityTypeSettings(string entityType)
        {
            if (_isLoadingEntitySettings)
                return;

            var settings = Config.EntityTypeSettings.GetSettings(entityType);
            settings.RenderDistance = (int)sldrEntityTypeRenderDistance.Value;
            settings.Information.Clear();

            foreach (CheckComboBoxItem item in ccbEntityInformation.SelectedItems)
            {
                settings.Information.Add(item.Content.ToString());
            }

            switch (entityType)
            {
                case "Corpse":
                    settings.ShowImportantLoot = chkShowImportantCorpseLoot.IsChecked == true;
                    break;
                case "Grenade":
                    settings.ShowRadius = chkExplosiveRadius.IsChecked == true;
                    break;
                case "Door":
                    settings.ShowLockedDoors = chkShowLockedDoors.IsChecked == true;
                    settings.ShowUnlockedDoors = chkShowUnlockedDoors.IsChecked == true;
                    break;
                case "Exfil":
                    settings.HideInactiveExfils = chkHideInactive.IsChecked == true;
                    break;
                case "Tripwire":
                    settings.ShowTripwireLine = chkShowTripwireLine.IsChecked == true;
                    break;
            }

            Config.Save();
            XMLogging.WriteLine($"Saved entity type settings for {entityType}");
        }

        public void RefreshQuestHelper()
        {
            if (Config.QuestHelper.Enabled && Memory.InRaid && Memory.QuestManager is QuestManager questManager)
            {
                Dispatcher.Invoke(() =>
                {
                    var existingIds = QuestItems.Select(q => q.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var newQuestItems = new List<QuestListItem>();

                    foreach (var questId in questManager.AllStartedQuestIds)
                    {
                        if (!existingIds.Contains(questId))
                        {
                            var enabled = !Config.QuestHelper.BlacklistedQuests.Contains(questId, StringComparer.OrdinalIgnoreCase);
                            newQuestItems.Add(new QuestListItem(questId, enabled));
                        }
                    }

                    newQuestItems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

                    foreach (var item in newQuestItems)
                    {
                        QuestItems.Add(item);
                    }

                    var activeQuestIds = questManager.AllStartedQuestIds;
                    for (int i = QuestItems.Count - 1; i >= 0; i--)
                    {
                        if (!activeQuestIds.Contains(QuestItems[i].Id))
                            QuestItems.RemoveAt(i);
                    }

                    var sortedItems = QuestItems.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    
                    QuestItems.Clear();
                    
                    foreach (var item in sortedItems)
                    {
                        QuestItems.Add(item);
                    }

                    listQuests.UpdateLayout();
                });
            }
        }
        private async void btnSendStashDogTags_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "? IMPORTANT ?\n\n" +
                "Make sure you are NOT in raid and are currently in the MAIN MENU.\n\n" +
                "Click Continue when ready, or Cancel to abort.",
                "Menu Stash Dogtag Dump",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning
            );
        
            if (result != MessageBoxResult.OK)
                return;
        
            btnSendStashDogTags.IsEnabled = false;
            btnSendStashDogTags.Content = "Sending...";
        
            try
            {
                await Task.Run(() =>
                {
                    MenuStashDogtagDumper.Dump();
                });
        
                NotificationsShared.Success(
                    "Menu stash dogtags successfully sent to API.\n\n" +
                    "You may now enter a raid safely."
                );
            }
            catch (Exception ex)
            {
                NotificationsShared.Error(
                    $"Failed to send dogtags:\n{ex.Message}"
                );
            }
            finally
            {
                btnSendStashDogTags.IsEnabled = true;
                btnSendStashDogTags.Content = "Send Stashed DogTags";
            }
        }
        private void InitializeWebRadar()
        {
            chkWebRadarUPnP.IsChecked = Config.WebRadar.UPnP;
            txtWebRadarPort.Text = Config.WebRadar.Port;


            if (WebRadarServer.IsRunning)
            {
                btnWebRadarStart.Content = "Stop";
                ToggleWebRadarControls(false);
            }
            else
            {
                btnWebRadarStart.Content = "Start";
                ToggleWebRadarControls(true);
            }
        }

        private void ToggleWebRadarControls(bool enabled = false)
        {
            btnWebRadarStart.IsEnabled = true;
            chkWebRadarUPnP.IsEnabled = enabled;
            txtWebRadarPort.IsEnabled = enabled;

        }

        private void ToggleMapSetup()
        {
            var cbo = chkMapSetup;
            var value = cbo.IsChecked == true;
            var panel = MainWindow.Window.MapSetupPanel;
            var config = XMMapManager.Map.Config;
            var mapControl = MainWindow.Window.MapSetupControl;

            if (value && Memory.InRaid && Memory.LocalPlayer != null)
                mapControl.UpdateMapConfiguration(config.X, config.Y, config.Scale);

            panel.Visibility = (panel.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateUIScale()
        {
            var newScale = (float)sldrUIScale.Value;
            var mainWindow = MainWindow.Window;
            mainWindow?.AimView?.SetScaleFactor(newScale);
            mainWindow?.PlayerInfo?.SetScaleFactor(newScale);
            mainWindow?.LootInfo?.SetScaleFactor(newScale);
            mainWindow?.DebugInfo?.SetScaleFactor(newScale);
            mainWindow?.QuestInfo?.SetScaleFactor(newScale);

            #region UpdatePaints

            // Outlines
            SKPaints.TextOutline.TextSize = 12f * newScale;
            SKPaints.TextOutline.StrokeWidth = 2f * newScale;
            // Shape Outline is computed before usage due to different stroke widths

            SKPaints.PaintConnectorGroup.StrokeWidth = 2.25f * newScale;
            SKPaints.PaintMouseoverGroup.StrokeWidth = 3 * newScale;
            SKPaints.TextMouseoverGroup.TextSize = 12 * newScale;
            SKPaints.PaintLocalPlayer.StrokeWidth = 3 * newScale;
            SKPaints.TextLocalPlayer.TextSize = 12 * newScale;
            SKPaints.PaintTeammate.StrokeWidth = 3 * newScale;
            SKPaints.TextTeammate.TextSize = 12 * newScale;
            SKPaints.PaintUSEC.StrokeWidth = 3 * newScale;
            SKPaints.TextUSEC.TextSize = 12 * newScale;
            SKPaints.PaintBEAR.StrokeWidth = 3 * newScale;
            SKPaints.TextBEAR.TextSize = 12 * newScale;
            SKPaints.PaintSpecial.StrokeWidth = 3 * newScale;
            SKPaints.TextSpecial.TextSize = 12 * newScale;
            SKPaints.PaintStreamer.StrokeWidth = 3 * newScale;
            SKPaints.TextStreamer.TextSize = 12 * newScale;
            SKPaints.PaintAimbotLocked.StrokeWidth = 3 * newScale;
            SKPaints.TextAimbotLocked.TextSize = 12 * newScale;
            SKPaints.PaintScav.StrokeWidth = 3 * newScale;
            SKPaints.TextScav.TextSize = 12 * newScale;
            SKPaints.PaintRaider.StrokeWidth = 3 * newScale;
            SKPaints.TextRaider.TextSize = 12 * newScale;
            SKPaints.PaintBoss.StrokeWidth = 3 * newScale;
            SKPaints.TextBoss.TextSize = 12 * newScale;
            SKPaints.PaintFocused.StrokeWidth = 3 * newScale;
            SKPaints.TextFocused.TextSize = 12 * newScale;
            SKPaints.PaintPScav.StrokeWidth = 3 * newScale;
            SKPaints.TextPScav.TextSize = 12 * newScale;
            SKPaints.TextMouseover.TextSize = 12 * newScale;
            SKPaints.PaintCorpse.StrokeWidth = 3 * newScale;
            SKPaints.TextCorpse.TextSize = 12 * newScale;
            SKPaints.PaintMeds.StrokeWidth = 3 * newScale;
            SKPaints.TextMeds.TextSize = 12 * newScale;
            SKPaints.PaintFood.StrokeWidth = 3 * newScale;
            SKPaints.TextFood.TextSize = 12 * newScale;
            SKPaints.PaintWeapons.StrokeWidth = 3 * newScale;
            SKPaints.TextWeapons.TextSize = 12 * newScale;
            SKPaints.PaintBackpacks.StrokeWidth = 3 * newScale;
            SKPaints.TextBackpacks.TextSize = 12 * newScale;
            SKPaints.PaintQuestItem.StrokeWidth = 3 * newScale;
            SKPaints.TextQuestItem.TextSize = 12 * newScale;
            SKPaints.PaintAirdrop.StrokeWidth = 3 * newScale;
            SKPaints.TextAirdrop.TextSize = 12 * newScale;
            SKPaints.PaintWishlistItem.StrokeWidth = 3 * newScale;
            SKPaints.TextWishlistItem.TextSize = 12 * newScale;
            SKPaints.QuestHelperPaint.StrokeWidth = 3 * newScale;
            SKPaints.QuestHelperText.TextSize = 12 * newScale;
            SKPaints.QuestHelperOutline.StrokeWidth = 2.25f * newScale;
            SKPaints.PaintDeathMarker.StrokeWidth = 3 * newScale;
            SKPaints.PaintLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintImportantLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintContainerLoot.StrokeWidth = 3 * newScale;
            SKPaints.TextContainer.TextSize = 12 * newScale;
            SKPaints.TextLoot.TextSize = 12 * newScale;
            SKPaints.TextImportantLoot.TextSize = 12 * newScale;
            SKPaints.PaintTransparentBacker.StrokeWidth = 1 * newScale;
            SKPaints.TextRadarStatus.TextSize = 48 * newScale;
            SKPaints.TextStatusSmall.TextSize = 13 * newScale;
            SKPaints.PaintExplosives.StrokeWidth = 3 * newScale;
            SKPaints.PaintExplosivesDanger.StrokeWidth = 3 * newScale;
            SKPaints.TextExplosives.TextSize = 12 * newScale;
            SKPaints.TextExplosivesDanger.TextSize = 12 * newScale;
            SKPaints.PaintExfilOpen.StrokeWidth = 3 * newScale;
            SKPaints.TextExfilOpen.TextSize = 12 * newScale;
            SKPaints.PaintExfilPending.StrokeWidth = 3 * newScale;
            SKPaints.TextExfilPending.TextSize = 12 * newScale;
            SKPaints.PaintExfilClosed.StrokeWidth = 3 * newScale;
            SKPaints.TextExfilClosed.TextSize = 12 * newScale;
            SKPaints.PaintExfilInactive.StrokeWidth = 3 * newScale;
            SKPaints.TextExfilInactive.TextSize = 12 * newScale;
            SKPaints.PaintExfilTransit.StrokeWidth = 3 * newScale;
            SKPaints.TextExfilTransit.TextSize = 12 * newScale;
            SKPaints.TextDoorOpen.TextSize = 12 * newScale;
            SKPaints.PaintDoorOpen.StrokeWidth = 3 * newScale;
            SKPaints.TextDoorLocked.TextSize = 12 * newScale;
            SKPaints.PaintDoorLocked.StrokeWidth = 3 * newScale;
            SKPaints.TextDoorShut.TextSize = 12 * newScale;
            SKPaints.PaintDoorShut.StrokeWidth = 3 * newScale;
            SKPaints.TextDoorInteracting.TextSize = 12 * newScale;
            SKPaints.PaintDoorInteracting.StrokeWidth = 3 * newScale;
            SKPaints.TextDoorBreaching.TextSize = 12 * newScale;
            SKPaints.PaintDoorBreaching.StrokeWidth = 3 * newScale;
            SKPaints.TextPulsingAsterisk.TextSize = 24 * newScale;
            SKPaints.TextPulsingAsteriskOutline.TextSize = 24 * newScale;
            SKPaints.PaintSwitch.StrokeWidth = 3 * newScale;
            SKPaints.TextSwitch.TextSize = 12 * newScale;
            #endregion
        }

        private void ModifyAllQuests(bool selectAll)
        {
            if (listQuests.ItemsSource != null)
            {
                foreach (QuestListItem item in listQuests.ItemsSource)
                {
                    item.IsSelected = selectAll;
                }
            }

            listQuests.Items.Refresh();
        }

        private void InitMonitors()
        {
            XMLogging.WriteLine("[InitMonitors] Starting monitor initialization...");
            if (!Memory.Ready)
            {
                XMLogging.WriteLine("[ERROR] Memory or Game is null, cannot initialize monitors.");
                return;
            }

            var gameRes = Memory.GetMonitorRes();
            XMLogging.WriteLine($"[InitMonitors] Game resolution: {gameRes.Width}x{gameRes.Height}");

            var monitors = MonitorHelper.GetAllMonitors();
            XMLogging.WriteLine($"[InitMonitors] Found {monitors.Count} monitor(s).");

            cboMonitor.Items.Clear();
            var selectedIndex = 0;

            for (int i = 0; i < monitors.Count; i++)
            {
                var mon = monitors[i];
                XMLogging.WriteLine($"[InitMonitors] Monitor {i + 1}: {mon.Bounds.Width}x{mon.Bounds.Height}");

                var isGame = (int)mon.Bounds.Width == gameRes.Width && (int)mon.Bounds.Height == gameRes.Height;
                if (isGame)
                    XMLogging.WriteLine($"[InitMonitors] Monitor {i + 1} matches game resolution and will be selected.");

                var label = isGame ? $"Game Monitor ({mon.Bounds.Width}x{mon.Bounds.Height})"
                                   : $"Monitor {i + 1} ({mon.Bounds.Width}x{mon.Bounds.Height})";

                var item = new ComboBoxItem
                {
                    Content = label,
                    Tag = i
                };

                cboMonitor.Items.Add(item);

                if (isGame)
                    selectedIndex = i;
            }

            if (cboMonitor.Items.Count > 0)
            {
                cboMonitor.SelectedIndex = selectedIndex;
                txtGameWidth.Text = monitors[selectedIndex].Bounds.Width.ToString();
                txtGameHeight.Text = monitors[selectedIndex].Bounds.Height.ToString();

                XMLogging.WriteLine($"[InitMonitors] Selected monitor index: {selectedIndex}");
            }
        }

        private void UpdateMonitorWH()
        {
            try
            {
                if (cboMonitor.SelectedIndex < 0 || cboMonitor.SelectedItem == null)
                    return;

                var monitors = MonitorHelper.GetAllMonitors();

                if (monitors == null || monitors.Count == 0)
                {
                    XMLogging.WriteLine("[UpdateMonitorWH] No monitors found");
                    return;
                }

                var selectedIndex = cboMonitor.SelectedIndex;

                if (selectedIndex >= monitors.Count)
                {
                    if (monitors.Count > 0)
                        selectedIndex = 0;
                    else
                        return;
                }

                var selectedMonitor = monitors[selectedIndex];
                var monitorWidth = selectedMonitor.Bounds.Width;
                var monitorHeight = selectedMonitor.Bounds.Height;

                Config.MonitorWidth = (int)monitorWidth;
                Config.MonitorHeight = (int)monitorHeight;

                txtGameWidth.Text = monitorWidth.ToString();
                txtGameHeight.Text = monitorHeight.ToString();

                CameraManagerBase.UpdateViewportRes();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ERROR] UpdateMonitorWH: {ex.Message}");
            }
        }

        private void ToggleQuestHelperControls()
        {
            var enabled = Config.QuestHelper.Enabled;

            chkKappaFilter.IsEnabled = enabled;
            chkOptionalTaskFilter.IsEnabled = enabled;
            chkKillZones.IsEnabled = enabled;
            chkQuestsSelectAll.IsEnabled = enabled;
            listQuests.IsEnabled = enabled;
        }

        private void SavePlayerTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentPlayerType) && !_isLoadingPlayerSettings)
                SavePlayerTypeSettings(_currentPlayerType);
        }

        private void SaveEntityTypeSettings()
        {
            if (!string.IsNullOrEmpty(_currentEntityType) && !_isLoadingEntitySettings)
                SaveEntityTypeSettings(_currentEntityType);
        }

        private void UpdateApiStatus()
        {
            var hasKey = ApiKeyStore.TryLoadApiKey(out _);

            if (hasKey)
            {
                txtApiStatus.Text = $"API key loaded successfully";
                btnCreateApiFile.Content = "Edit API Fileï¿?";
                btnCreateApiFile.ToolTip = "Replace the stored API key";
                btnClearApiFile.IsEnabled = true;
                btnOpenApiFolder.IsEnabled = true;
            }
            else
            {
                txtApiStatus.Text = "No API key saved.";
                btnCreateApiFile.Content = "Create API Fileï¿?";
                btnCreateApiFile.ToolTip = "Create and store an API key securely";
                btnClearApiFile.IsEnabled = false;
                btnOpenApiFolder.IsEnabled = false;
            }
        }

        private void LoadApiSettings()
        {
            _suppressApiEvents = true;

            try
            {
                rdbEftApiTech.IsChecked = Config.AlternateProfileService;
                rdbTarkovDev.IsChecked = !Config.AlternateProfileService;
                UpdateApiStatus();
            }
            finally
            {
                _suppressApiEvents = false;
            }
        }
        #endregion

        #region Events
        private void GeneralCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cbo && cbo.Tag is string tag)
            {
                var value = cbo.IsChecked == true;

                XMLogging.WriteLine($"[Checkbox] {cbo.Name} changed to {value}");

                switch (tag)
                {
                    case "ShowMapSetup":
                        ToggleMapSetup();
                        break;
                    case "PlayerHeightIndicator":
                    case "ImportantIndicator":
                    case "ShowImportantPlayerLoot":
                    case "HighAlert":
                        SavePlayerTypeSettings();
                        break;
                    case "ShowImportantCorpseLoot":
                    case "ShowExplosiveRadius":
                    case "ShowLockedDoors":
                    case "ShowUnlockedDoors":
                    case "HideInactiveExfils":
                    case "ShowTripwireLine":
                        SaveEntityTypeSettings();
                        break;
                    case "RefreshMonitors":
                        InitMonitors();
                        break;
                    case "QuestHelper":
                        Config.QuestHelper.Enabled = value;
                        ToggleQuestHelperControls();
                        RefreshQuestHelper();
                        break;
                    case "QuestsSelectAll":
                        ModifyAllQuests(value);
                        break;
                    case "KappaFilter":
                        Config.QuestHelper.KappaFilter = value;
                        break;
                    case "OptionalTaskFilter":
                        Config.QuestHelper.OptionalTaskFilter = value;
                        break;
                    case "KillZones":
                        Config.QuestHelper.KillZones = value;
                        break;
                    case "UPnP":
                        Config.WebRadar.UPnP = value;
                        break;
                    case "EnableApi":
                        Config.AlternateProfileService = value;
                        break;
                }

                Config.Save();
                XMLogging.WriteLine("Saved Config");
            }
        }

        private void GeneralRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rdb && rdb.Tag is string tag)
            {
                var isChecked = rdb.IsChecked == true;
                XMLogging.WriteLine($"[RadioButton] {rdb.Name} changed to {isChecked}");

                if (isChecked)
                {
                    switch (tag)
                    {
                        case "TarkovDev":
                            if (_suppressApiEvents || !_uiReady)
                                return;

                            Config.AlternateProfileService = false;

                            UpdateApiStatus();
                            break;
                        case "EftApiTech":
                            if (_suppressApiEvents || !_uiReady)
                                return;

                            Config.AlternateProfileService = true;

                            UpdateApiStatus();
                            break;
                    }
                    Config.Save();
                    XMLogging.WriteLine("Saved Config");
                }
            }
        }

        private void GeneralSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (sender is TextValueSlider sldr && sldr.Tag is string tag)
            {
                var value = sldr.Value;

                switch (tag)
                {
                    case "UIScale":
                        Config.UIScale = (float)value;
                        UpdateUIScale();
                        break;
                    case "AimlineLength":
                        SavePlayerTypeSettings();
                        break;
                }

                Config.Save();
            }
        }

        private void GeneralTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is HandyControl.Controls.TextBox txt && txt.Tag is string tag)
            {
                var text = txt.Text.Trim();
                int.TryParse(text, out int intValue);

                switch (tag)
                {
                    case "GameWidth":
                        Config.MonitorWidth = intValue;
                        CameraManagerBase.UpdateViewportRes();
                        break;
                    case "GameHeight":
                        Config.MonitorHeight = intValue;
                        CameraManagerBase.UpdateViewportRes();
                        break;
                    case "WebRadarClientURL":
                        Config.WebRadar.WebClientURL = text;
                        break;
                    case "WebRadarPort":
                        Config.WebRadar.Port = text;
                        break;
                }

                Config.Save();
                XMLogging.WriteLine("Saved Config");
            }
        }

        private void GeneralComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is HandyControl.Controls.ComboBox cbo && cbo.Tag is string tag)
            {
                switch (tag)
                {
                    case "Monitor":
                        Config.ESP.SelectedScreen = cbo.SelectedIndex;
                        UpdateMonitorWH();
                        break;
                }

                Config.Save();
                XMLogging.WriteLine("[ComboBox] Selection changed and config saved.");
            }
        }

        private void GeneralSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is TextValueSlider slider && slider.Tag is string tag)
            {
                var intValue = (int)e.NewValue;
                var floatValue = (float)e.NewValue;
                switch (tag)
                {
                    case "UIScale":
                        Config.UIScale = floatValue;
                        UpdateUIScale();
                        break;
                    case "PlayerTypeRenderDistance":
                    case "PlayerTypeAimlineLength":
                    case "MinimumKD":
                        SavePlayerTypeSettings(); break;
                    case "EntityTypeRenderDistance":
                        SaveEntityTypeSettings(); break;
                }

                Config.Save();
            }
        }

        private void GeneralNUD_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (sender is HandyControl.Controls.NumericUpDown nud && nud.Tag is string tag && e.Info is double value)
            {
                var intValue = (int)value;

                switch (tag)
                {
                    case "FPSLimit":
                        Config.RadarTargetFPS = intValue;
                        Config.Save();
                        MainWindow.Window.UpdateRenderTimerInterval(intValue);
                        break;
                }

                Config.Save();
            }
        }

        private void QuestListItem_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is QuestListItem item)
            {
                var isChecked = checkBox.IsChecked == true;
                var id = item.Id.ToLower();

                if (isChecked)
                    Config.QuestHelper.BlacklistedQuests.Remove(id);
                else
                    Config.QuestHelper.BlacklistedQuests.Add(id);
            }
        }

        private void lblWebRadarLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var link = lblWebRadarLink.Text;

            if (string.IsNullOrWhiteSpace(link))
                return;

            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch { }
        }

        private async void btnWebRadarStart_Click(object sender, RoutedEventArgs e)
        {
            if (WebRadarServer.IsRunning)
            {
                ToggleWebRadarControls(false);
                btnWebRadarStart.Content = "Stopping...";

                try
                {
                    await WebRadarServer.StopAsync();

                    btnWebRadarStart.Content = "Start";
                    lblWebRadarLink.Text = "";
                    ToggleWebRadarControls(true);

                    NotificationsShared.Info("Web Radar Server stopped successfully.");
                }
                catch (Exception ex)
                {
                    NotificationsShared.Error($"ERROR Stopping Web Radar Server: {ex.Message}");
                    btnWebRadarStart.Content = "Stop";
                    ToggleWebRadarControls(true);
                }
            }
            else
            {
                ToggleWebRadarControls(false);
                btnWebRadarStart.Content = "Starting...";

                try
                {
                    var tickRate = TimeSpan.FromMilliseconds(1000d / 60);
                    var bindIP = "0.0.0.0";
                    var port = int.Parse(txtWebRadarPort.Text.Trim());
                    var useUPnP = chkWebRadarUPnP.IsChecked == true;


                    await WebRadarServer.StartAsync(bindIP, port, tickRate, useUPnP);

                    btnWebRadarStart.Content = "Stop";

                    var externalIP = await WebRadarServer.GetExternalIPAsync();
                    var webClientUrl = $"http://{externalIP}";

                    lblWebRadarLink.Text = $"{webClientUrl}:{port}";

                    NotificationsShared.Success("Web Radar Server started successfully!");
                }
                catch (Exception ex)
                {
                    NotificationsShared.Error($"ERROR Starting Web Radar Server: {ex.Message}");
                    btnWebRadarStart.Content = "Start";
                    ToggleWebRadarControls(true);
                }
            }
        }

        private void btnAutoDetectIP_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var localIP = WebRadarServer.GetLocalIPAddress();

                if (!string.IsNullOrEmpty(localIP))
                {
                    Config.WebRadar.IP = localIP;
                    Config.Save();

                    NotificationsShared.Success($"Auto-detected local IP: {localIP}");
                    XMLogging.WriteLine($"[AutoDetectIP] Found local IP: {localIP}");
                }
                else
                {
                    NotificationsShared.Warning("Could not auto-detect local IP address. Please enter manually.");
                    XMLogging.WriteLine("[AutoDetectIP] Failed to detect local IP");
                }
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"Error auto-detecting IP: {ex.Message}");
                XMLogging.WriteLine($"[AutoDetectIP] Error: {ex.Message}");
            }
        }

        private void btnRefreshMonitors_Click(object sender, RoutedEventArgs e)
        {
            InitMonitors();
        }


        private async void btnCreateApiFile_Click(object sender, RoutedEventArgs e)
        {
            btnCreateApiFile.IsEnabled = false;

            try
            {
                var exists = File.Exists(ApiKeyStore.StorePath);

                if (exists)
                {
                    var res = MessageBox.Show(
                        "An API key already exists. Do you want to replace it?",
                        "Replace API Key",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (res != MessageBoxResult.Yes)
                        return;
                }

                var key = await ApiKeyWizard.CaptureApiKeyAsync();
                if (string.IsNullOrWhiteSpace(key))
                    return;

                ApiKeyStore.SaveApiKey(key);

                UpdateApiStatus();

                MessageBox.Show(
                    exists ? "API key updated successfully." : "API key saved securely (encrypted).",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save API key:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnCreateApiFile.IsEnabled = true;
            }
        }


        private void btnOpenApiFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(ApiKeyStore.StoreDir);
                Process.Start("explorer.exe", ApiKeyStore.StoreDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnClearApiFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(ApiKeyStore.StorePath))
                    File.Delete(ApiKeyStore.StorePath);

                UpdateApiStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete api.json:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void widgetsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbWidgets.Items)
            {
                var widgetOption = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (widgetOption)
                {
                    case "Aimview Widget":
                        Config.AimviewWidgetEnabled = isSelected;
                        break;
                    case "Debug Widget":
                        Config.ShowDebugWidget = isSelected;
                        break;
                    case "Player Info Widget":
                        Config.ShowInfoTab = isSelected;
                        break;
                    case "Loot Info Widget":
                        Config.ShowLootInfoWidget = isSelected;
                        break;
                    case "Quest Info Widget":
                        Config.ShowQuestInfoWidget = isSelected;
                        break;
                    case "HotKey Info Widget":
                        Config.ESP.ShowHotkeyInfoWidget = isSelected;
                        break;
                }
            }

            Config.Save();
            XMLogging.WriteLine("Saved widget settings");
        }

        private void generalOptionsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettingAndWidgets)
                return;

            foreach (CheckComboBoxItem item in ccbGeneralOptions.Items)
            {
                var option = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (option)
                {
                    case "Connect Groups":
                        Config.ConnectGroups = isSelected;
                        break;
                    case "Mask Names":
                        Config.MaskNames = isSelected;
                        break;
                    case "Players on Top":
                        Config.PlayersOnTop = isSelected;
                        break;
                }
            }

            Config.Save();
            XMLogging.WriteLine("Saved general options settings");
        }

        private void cboPlayerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboPlayerType.SelectedItem is ComboBoxItem item)
            {
                SavePlayerTypeSettings();

                _currentPlayerType = item.Tag.ToString();
                LoadPlayerTypeSettings(_currentPlayerType);
            }
        }

        private void playerInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SavePlayerTypeSettings();
            UpdatePlayerInformationControlsVisibility();
        }

        private void cboEntityType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboEntityType.SelectedItem is ComboBoxItem item)
            {
                SaveEntityTypeSettings();

                _currentEntityType = item.Tag.ToString();
                LoadEntityTypeSettings(_currentEntityType);
            }
        }

        private void entityInfoCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveEntityTypeSettings();
        }
        #endregion
        #endregion

        #region Colors Tab
        #region Functions/Methods
        private void RegisterColorEvents()
        {
            // Players
            btnLocalPlayerColor.Click += ColorButton_Clicked;
            btnFriendlyColor.Click += ColorButton_Clicked;
            btnUSECColor.Click += ColorButton_Clicked;
            btnBEARColor.Click += ColorButton_Clicked;
            btnPlayerScavColor.Click += ColorButton_Clicked;
            btnStreamerColor.Click += ColorButton_Clicked;
            btnFocusedColor.Click += ColorButton_Clicked;
            btnSpecialColor.Click += ColorButton_Clicked;
            btnRaiderColor.Click += ColorButton_Clicked;
            btnBossColor.Click += ColorButton_Clicked;
            btnScavColor.Click += ColorButton_Clicked;
            btnAimbotTargetColor.Click += ColorButton_Clicked;
            btnVisibleColor.Click += ColorButton_Clicked;

            // Loot
            btnRegularLootColor.Click += ColorButton_Clicked;
            btnValuableLootColor.Click += ColorButton_Clicked;
            btnWishlistLootColor.Click += ColorButton_Clicked;
            btnContainerLootColor.Click += ColorButton_Clicked;
            btnMedsFilterLootColor.Click += ColorButton_Clicked;
            btnFoodFilterLootColor.Click += ColorButton_Clicked;
            btnBackpackFilterLootColor.Click += ColorButton_Clicked;
            btnWeaponsFilterLootColor.Click += ColorButton_Clicked;
            btnQuestLootColor.Click += ColorButton_Clicked;
            btnAirdropsColor.Click += ColorButton_Clicked;
            btnQuestItemsAndZonesColor.Click += ColorButton_Clicked;

            // Other
            btnDeathMarkerColor.Click += ColorButton_Clicked;
            btnCorpseColor.Click += ColorButton_Clicked;
            btnExplosivesColor.Click += ColorButton_Clicked;
            btnSwitchesColor.Click += ColorButton_Clicked;
            btnQuestKillZoneColor.Click += ColorButton_Clicked;
            btnExfilOpenColor.Click += ColorButton_Clicked;
            btnExfilPendingColor.Click += ColorButton_Clicked;
            btnExfilClosedColor.Click += ColorButton_Clicked;
            btnExfilInactiveColor.Click += ColorButton_Clicked;
            btnExfilTransitColor.Click += ColorButton_Clicked;
            btnDoorOpenColor.Click += ColorButton_Clicked;
            btnDoorLockedColor.Click += ColorButton_Clicked;
            btnDoorShutColor.Click += ColorButton_Clicked;
            btnGroupLinesColor.Click += ColorButton_Clicked;

            // Fuser HUD Colors
            btnFPSColor.Click += ColorButton_Clicked;
            btnRaidStatsColor.Click += ColorButton_Clicked;
            btnStatusTextColor.Click += ColorButton_Clicked;
            btnMagazineInfoColor.Click += ColorButton_Clicked;
            btnEnergyBarColor.Click += ColorButton_Clicked;
            btnHydrationBarColor.Click += ColorButton_Clicked;
            btnCrosshairColor.Click += ColorButton_Clicked;
            btnFireportAimColor.Click += ColorButton_Clicked;
            btnAimbotFOVColor.Click += ColorButton_Clicked;
            btnAimbotLockColor.Click += ColorButton_Clicked;
            btnClosestPlayerColor.Click += ColorButton_Clicked;
            btnTopLootColor.Click += ColorButton_Clicked;
            btnMiniRadarThemeColor.Click += ColorButton_Clicked;

            // Interface
            btnAccentColor.Click += ColorButton_Clicked;
            btnRegionColor.Click += ColorButton_Clicked;
            btnSecondaryRegionColor.Click += ColorButton_Clicked;
            btnBorderColor.Click += ColorButton_Clicked;
            btnRadarBackgroundColor.Click += ColorButton_Clicked;
            btnFuserBackgroundColor.Click += ColorButton_Clicked;
        }

        private void LoadColorSettings()
        {
            InitializeBrushFields();

            foreach (var colorPair in Config.Colors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = colorOption.ToString();

                if (_brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;

                            var colorDict = new Dictionary<RadarColorOption, string>
                            {
                                [colorOption] = colorValue
                            };
                            RadarColorOptions.SetColors(colorDict);
                        }
                    }
                    catch (Exception) { }
                }
            }

            foreach (var colorPair in Config.ESP.Colors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = colorOption.ToString();

                if (_brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;

                            var colorDict = new Dictionary<EspColorOption, string>
                            {
                                [colorOption] = colorValue
                            };
                            EspColorOptions.SetColors(colorDict);
                        }
                    }
                    catch (Exception) { }
                }
            }

            foreach (var colorPair in Config.InterfaceColors)
            {
                var colorOption = colorPair.Key;
                var colorValue = colorPair.Value;
                var tagName = InterfaceColorOptions.GetTagFromOption(colorOption);

                if (!string.IsNullOrEmpty(tagName) && _brushFields.TryGetValue(tagName, out var brush))
                {
                    try
                    {
                        var color = ColorConverter.ConvertFromString(colorValue);
                        if (color != null)
                        {
                            brush.Color = (Color)color;
                            InterfaceColorOptions.UpdateColor(Config, colorOption, (Color)color);
                        }
                    }
                    catch (Exception) { }
                }
            }

            InterfaceColorOptions.LoadColors(Config);
            Config.Save();
        }

        private void InitializeBrushFields()
        {
            // Player colors
            _brushFields["LocalPlayer"] = localPlayerBrush;
            _brushFields["Friendly"] = friendlyBrush;
            _brushFields["USEC"] = USECBrush;
            _brushFields["BEAR"] = BEARBrush;
            _brushFields["PlayerScav"] = playerScavBrush;
            _brushFields["Streamer"] = streamerBrush;
            _brushFields["Special"] = specialBrush;
            _brushFields["Focused"] = focusedBrush;
            _brushFields["Raider"] = raiderBrush;
            _brushFields["Boss"] = bossBrush;
            _brushFields["Scav"] = scavBrush;
            _brushFields["AimbotTarget"] = aimbotTargetBrush;
            _brushFields["Visible"] = visibileBrush;

            // Loot colors
            _brushFields["RegularLoot"] = regularLootBrush;
            _brushFields["ValuableLoot"] = valuableLootBrush;
            _brushFields["WishlistLoot"] = wishlistLootBrush;
            _brushFields["ContainerLoot"] = containerLootBrush;
            _brushFields["MedsFilterLoot"] = medsFilterLootBrush;
            _brushFields["FoodFilterLoot"] = foodFilterLootBrush;
            _brushFields["WeaponsFilterLoot"] = weaponsFilterLootBrush;
            _brushFields["BackpackFilterLoot"] = backpackFilterLootBrush;
            _brushFields["QuestLoot"] = questLootBrush;
            _brushFields["Airdrops"] = airdropsBrush;
            _brushFields["StaticQuestItemsAndZones"] = questItemsAndZonesBrush;

            // Other colors
            _brushFields["DeathMarker"] = deathMarkerBrush;
            _brushFields["Corpse"] = corpseBrush;
            _brushFields["Explosives"] = explosivesBrush;
            _brushFields["Switches"] = switchesBrush;
            _brushFields["QuestKillZone"] = questKillZoneBrush;
            _brushFields["ExfilOpen"] = exfilOpenBrush;
            _brushFields["ExfilPending"] = exfilPendingBrush;
            _brushFields["ExfilClosed"] = exfilClosedBrush;
            _brushFields["ExfilInactive"] = exfilInactiveBrush;
            _brushFields["ExfilTransit"] = exfilTransitBrush;
            _brushFields["DoorOpen"] = doorOpenBrush;
            _brushFields["DoorLocked"] = doorLockedBrush;
            _brushFields["DoorShut"] = doorShutBrush;
            _brushFields["GroupLines"] = groupLinesBrush;

            // Fuser HUD colors
            _brushFields["FPS"] = FPSBrush;
            _brushFields["RaidStats"] = raidStatsBrush;
            _brushFields["StatusText"] = statusTextBrush;
            _brushFields["MagazineInfo"] = magazineInfoBrush;
            _brushFields["EnergyBar"] = energyBarBrush;
            _brushFields["HydrationBar"] = hydrationBarBrush;
            _brushFields["Crosshair"] = crosshairBrush;
            _brushFields["FireportAim"] = fireportAimBrush;
            _brushFields["AimbotFOV"] = aimbotFOVBrush;
            _brushFields["AimbotLock"] = aimbotLockBrush;
            _brushFields["ClosestPlayer"] = closestPlayerBrush;
            _brushFields["TopLoot"] = topLootBrush;
            _brushFields["MiniRadarTheme"] = miniRadarThemeBrush;

            // Interface colors
            _brushFields["Interface.Accent"] = accentColor;
            _brushFields["Interface.Region"] = regionColor;
            _brushFields["Interface.SecondaryRegion"] = secondaryRegionColor;
            _brushFields["Interface.Border"] = borderColor;
            _brushFields["Interface.RadarBackground"] = radarBackgroundColor;
            _brushFields["Interface.FuserBackground"] = fuserBackgroundColor;
        }

        private void UpdateColor(string tag, SolidColorBrush brush)
        {
            if (_brushFields.TryGetValue(tag, out var fieldBrush))
                fieldBrush.Color = brush.Color;

            if (tag.StartsWith("Interface."))
            {
                if (InterfaceColorOptions.TryGetColorOption(tag, out var option))
                    InterfaceColorOptions.UpdateColor(Config, option, brush.Color);
                return;
            }

            if (Enum.TryParse<EspColorOption>(tag, out var espOption))
            {
                var hexColor = brush.Color.ToString();

                if (Config.ESP.Colors == null)
                    Config.ESP.Colors = new Dictionary<EspColorOption, string>();

                Config.ESP.Colors[espOption] = hexColor;

                var colorDict = new Dictionary<EspColorOption, string>
                {
                    [espOption] = hexColor
                };

                EspColorOptions.SetColors(colorDict);
                Config.Save();
            }

            if (Enum.TryParse<RadarColorOption>(tag, out var radarOption))
            {
                var hexColor = brush.Color.ToString();

                if (Config.Colors == null)
                    Config.Colors = new Dictionary<RadarColorOption, string>();

                Config.Colors[radarOption] = hexColor;

                var colorDict = new Dictionary<RadarColorOption, string>
                {
                    [radarOption] = hexColor
                };

                RadarColorOptions.SetColors(colorDict);
                Config.Save();
            }
        }

        private SolidColorBrush OpenColorPicker(Button sourceButton, SolidColorBrush currentBrush, string tag)
        {
            _openColorPicker?.Close();

            SolidColorBrush actualBrush = sourceButton.Background as SolidColorBrush;
            if (actualBrush == null)
                actualBrush = currentBrush;

            var picker = HandyControl.Tools.SingleOpenHelper.CreateControl<HandyControl.Controls.ColorPicker>();
            picker.SelectedBrush = actualBrush;

            var window = new HandyControl.Controls.PopupWindow
            {
                PopupElement = picker,
                AllowsTransparency = true,
                WindowStyle = WindowStyle.None,
                MinWidth = 0,
                MinHeight = 0
            };

            _openColorPicker = window;

            var originalBrush = actualBrush.Clone();
            var resultBrush = actualBrush;
            var confirmed = false;

            var parentWindow = MainWindow.GetWindow(sourceButton);
            var generalSettingsPanel = parentWindow?.FindName("GeneralSettingsPanel") as FrameworkElement;

            void UpdatePickerPosition()
            {
                try
                {
                    var buttonPos = sourceButton.PointToScreen(new Point(0, 0));
                    var leftPos = buttonPos.X + sourceButton.ActualWidth - 5;
                    var topPos = buttonPos.Y - sourceButton.ActualHeight - 5;

                    window.Left = leftPos;
                    window.Top = topPos;
                }
                catch { }
            }

            picker.SelectedColorChanged += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                    sourceButton.Background = picker.SelectedBrush;
            };

            picker.Confirmed += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                {
                    resultBrush = picker.SelectedBrush as SolidColorBrush;
                    confirmed = true;

                    UpdateColor(tag, resultBrush);
                }
                window.Close();
            };

            picker.Canceled += (s, args) =>
            {
                sourceButton.Background = originalBrush;
                window.Close();
            };

            EventHandler parentLocationChanged = (s, e) => UpdatePickerPosition();
            SizeChangedEventHandler parentSizeChanged = (s, e) => UpdatePickerPosition();
            EventHandler panelLayoutUpdated = (s, e) => UpdatePickerPosition();

            if (parentWindow != null)
            {
                parentWindow.LocationChanged += parentLocationChanged;
                parentWindow.SizeChanged += parentSizeChanged;
            }

            if (generalSettingsPanel != null)
            {
                generalSettingsPanel.LayoutUpdated += panelLayoutUpdated;
            }

            window.Loaded += (s, e) =>
            {
                UpdatePickerPosition();
            };

            window.Closed += (s, e) =>
            {
                _openColorPicker = null;

                if (parentWindow != null)
                {
                    parentWindow.LocationChanged -= parentLocationChanged;
                    parentWindow.SizeChanged -= parentSizeChanged;
                }

                if (generalSettingsPanel != null)
                {
                    generalSettingsPanel.LayoutUpdated -= panelLayoutUpdated;
                }
            };

            window.Show(sourceButton, false);

            return confirmed ? resultBrush : null;
        }
        #endregion
        #region Events
        private void ColorButton_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (_brushFields.TryGetValue(tag, out var brush))
                    OpenColorPicker(btn, brush, tag);
            }
        }
        #endregion
        #endregion

        #region Hotkeys Tab
        #region Functions/Methods
        private void LoadHotkeySettings()
        {
            hotkeyListView.ItemsSource = _hotkeyList;

            LoadHotkeyActions();
            LoadHotkeysFromConfig();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                RegisterHotkeyHandlers();
            }), DispatcherPriority.Background);
        }

        private void RegisterHotkeyEvents()
        {
            btnAddHotkey.Click += btnAddHotkey_Click;
            btnRemoveHotkey.Click += btnRemoveHotkey_Click;

            cboAction.PreviewKeyDown += cboAction_PreviewKeyDown;
            keyInputBox.CapturingStateChanged += KeyInputBox_CapturingStateChanged;
        }
        private void KeyInputBox_CapturingStateChanged(object sender, bool isCapturing)
        {
            _keyInputBoxIsCapturing = isCapturing;
        }

        private void cboAction_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_keyInputBoxIsCapturing)
                e.Handled = true;
        }
        private void RegisterHotkeyHandlers()
        {
            if (!InputManager.IsReady)
            {
                XMLogging.WriteLine("[Hotkeys] InputManager not ready, retrying hotkey registration");

                Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        RegisterHotkeyHandlers();
                    }), DispatcherPriority.Background);
                });

                return;
            }

            UnregisterAllHotkeyHandlers();

            var registeredCount = 0;
            var allHotkeys = GetAllHotkeys().ToList();
            XMLogging.WriteLine($"[Hotkeys] Found {allHotkeys.Count} total hotkey entries in config");
            
            foreach (var (actionKey, entry) in allHotkeys)
            {
                if (entry.Enabled && entry.Key != -1)
                {
                    XMLogging.WriteLine($"[Hotkeys] Registering: {actionKey} -> Key {entry.Key} (Mode: {entry.Mode})");
                    var capturedActionKey = actionKey;
                    var capturedEntry = entry;
                    var actionName = $"{actionKey}_{DateTime.Now.Ticks}";
                    var actionId = InputManager.RegisterKeyAction(entry.Key, actionName, (sender, e) =>
                    {
                        XMLogging.WriteLine($"[Hotkeys] Key event: {capturedActionKey} IsPressed={e.IsPressed}");
                        HandleHotkeyEvent(capturedActionKey, capturedEntry, e);
                    });

                    if (actionId != -1)
                    {
                        if (!_actionKeyMappings.ContainsKey(actionKey))
                            _actionKeyMappings[actionKey] = new List<int>();

                        _actionKeyMappings[actionKey].Add(actionId);
                        _actionIdToKeyMap[actionId] = actionKey;
                        registeredCount++;
                        XMLogging.WriteLine($"[Hotkeys] OK Registered {actionKey} with actionId {actionId}");
                    }
                    else
                    {
                        XMLogging.WriteLine($"[Hotkeys] FAILED to register hotkey for {actionKey} (Key: {entry.Key})");
                    }
                }
            }

            XMLogging.WriteLine($"[Hotkeys] Registered {registeredCount} hotkey handlers");
        }

        private void UnregisterAllHotkeyHandlers()
        {
            foreach (var actionIds in _actionKeyMappings.Values)
            {
                foreach (var actionId in actionIds)
                {
                    InputManager.UnregisterKeyAction(actionId);
                }
            }

            _actionKeyMappings.Clear();
            _actionIdToKeyMap.Clear();
        }

        private void HandleHotkeyEvent(string actionKey, HotkeyEntry entry, InputManager.KeyEventArgs e)
        {
            XMLogging.WriteLine($"[Hotkeys] HandleHotkeyEvent: {actionKey} IsPressed={e.IsPressed} Mode={entry.Mode}");
            
            if (_lastExecutionTime.TryGetValue(actionKey, out var lastTime))
            {
                var elapsed = (DateTime.UtcNow - lastTime).TotalMilliseconds;
                if (elapsed < HOTKEY_COOLDOWN_MS)
                {
                    XMLogging.WriteLine($"[Hotkeys] Cooldown active for {actionKey} ({elapsed:F0}ms < {HOTKEY_COOLDOWN_MS}ms)");
                    return;
                }
            }

            switch (entry.Mode)
            {
                case HotkeyMode.Toggle:
                    if (e.IsPressed)
                    {
                        var currentState = _toggleStates.GetValueOrDefault(actionKey);
                        var newState = !currentState;
                        _toggleStates[actionKey] = newState;
                        XMLogging.WriteLine($"[Hotkeys] Toggle {actionKey}: {currentState} -> {newState}");

                        Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, newState));
                        _lastExecutionTime[actionKey] = DateTime.UtcNow;
                    }
                    break;

                case HotkeyMode.OnKey:
                    if (IsContinuousAction(actionKey))
                    {
                        if (e.IsPressed)
                        {
                            XMLogging.WriteLine($"[Hotkeys] OnKey (continuous) {actionKey}: true");
                            Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, true));
                            _lastExecutionTime[actionKey] = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        XMLogging.WriteLine($"[Hotkeys] OnKey {actionKey}: {e.IsPressed}");
                        Dispatcher.Invoke(() => ExecuteHotkeyAction(actionKey, e.IsPressed));
                        _lastExecutionTime[actionKey] = DateTime.UtcNow;
                    }
                    break;
            }
        }

        private string GetKeyDisplayName(int keyCode)
        {
            if (keyCode >= 0x01 && keyCode <= 0x06)
            {
                return keyCode switch
                {
                    0x01 => "Mouse1",
                    0x02 => "Mouse2",
                    0x04 => "Mouse3",
                    0x05 => "Mouse4",
                    0x06 => "Mouse5",
                    _ => $"Mouse{keyCode}"
                };
            }
            else if (keyCode > 0)
            {
                try
                {
                    var key = KeyInterop.KeyFromVirtualKey(keyCode);
                    return GetKeyName(key);
                }
                catch
                {
                    return $"Key{keyCode}";
                }
            }

            return "None";
        }

        private string GetKeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return (key - Key.D0).ToString();

            return key switch
            {
                Key.LeftAlt => "LeftAlt",
                Key.RightAlt => "RightAlt",
                Key.LeftCtrl => "LeftCtrl",
                Key.RightCtrl => "RightCtrl",
                Key.LeftShift => "LeftShift",
                Key.RightShift => "RightShift",
                Key.LWin => "LeftWin",
                Key.RWin => "RightWin",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Insert => "Insert",
                Key.Home => "Home",
                Key.End => "End",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                Key.Escape => "Escape",
                Key.CapsLock => "CapsLock",
                Key.NumLock => "NumLock",
                Key.Scroll => "ScrollLock",
                Key.PrintScreen => "PrintScreen",
                Key.Pause => "Pause",
                Key.NumPad0 => "Numpad0",
                Key.NumPad1 => "Numpad1",
                Key.NumPad2 => "Numpad2",
                Key.NumPad3 => "Numpad3",
                Key.NumPad4 => "Numpad4",
                Key.NumPad5 => "Numpad5",
                Key.NumPad6 => "Numpad6",
                Key.NumPad7 => "Numpad7",
                Key.NumPad8 => "Numpad8",
                Key.NumPad9 => "Numpad9",
                Key.Multiply => "Numpad*",
                Key.Add => "Numpad+",
                Key.Subtract => "Numpad-",
                Key.Divide => "Numpad/",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.Oem1 => ";",
                Key.Oem2 => "/",
                Key.Oem3 => "`",
                Key.Oem4 => "[",
                Key.Oem5 => "\\",
                Key.Oem6 => "]",
                Key.Oem7 => "'",
                _ => key.ToString()
            };
        }

        private void LoadHotkeyActions()
        {
            XMLogging.WriteLine("[HotkeyCombo] Loading available hotkey actions...");

            AvailableHotkeyActions.Clear();

            foreach (PropertyInfo prop in typeof(HotkeyConfig).GetProperties())
            {
                var displayName = SplitCamelCase(prop.Name);
                AvailableHotkeyActions.Add(new HotkeyActionModel
                {
                    Key = prop.Name,
                    Name = displayName
                });
            }

            cboAction.ItemsSource = AvailableHotkeyActions;
        }

        private void LoadHotkeysFromConfig()
        {
            if (Config == null)
                return;

            _hotkeyList.Clear();
            _toggleStates.Clear();

            var config = Config.HotKeys;
            var properties = typeof(HotkeyConfig).GetProperties();

            foreach (var prop in properties)
            {
                if (prop.GetValue(config) is HotkeyEntry entry && entry.Enabled && entry.Key != -1)
                {
                    var actionKey = prop.Name;
                    var displayName = actionKey;
                    var model = AvailableHotkeyActions.FirstOrDefault(a => a.Key == actionKey);
                    if (model != null)
                        displayName = model.Name;

                    var keyString = GetKeyDisplayName(entry.Key);

                    _hotkeyList.Add(new HotkeyDisplayModel
                    {
                        Action = displayName,
                        Key = keyString,
                        Type = entry.Mode == HotkeyMode.Toggle ? "Toggle" : "OnKey"
                    });

                    if (entry.Mode == HotkeyMode.Toggle)
                        _toggleStates[actionKey] = false;
                }
            }
        }

        private bool IsContinuousAction(string actionKey)
        {
            return actionKey switch
            {
                nameof(HotkeyConfig.ZoomIn) => true,
                nameof(HotkeyConfig.ZoomOut) => true,
                nameof(HotkeyConfig.MiniRadarZoomIn) => true,
                nameof(HotkeyConfig.MiniRadarZoomOut) => true,
                _ => false
            };
        }

        private string SplitCamelCase(string input)
        {
            return Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
        }

        private IEnumerable<(string ActionKey, HotkeyEntry Entry)> GetAllHotkeys()
        {
            var config = Config.HotKeys;
            var props = typeof(HotkeyConfig).GetProperties();

            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(HotkeyEntry))
                {
                    var entry = prop.GetValue(config) as HotkeyEntry;

                    if (entry != null)
                        yield return (prop.Name, entry);
                }
            }
        }

        private void ExecuteHotkeyAction(string actionKey, bool isActive)
        {
            switch (actionKey)
            {
                #region Testing
                //case nameof(HotkeyConfig.TestAction):
                    //XMLogging.WriteLine($"Test action executed! IsOffline: {Memory.IsOffline}");
                    //break;
                //case nameof(HotkeyConfig.TestAction2):
                  // try
                  // {
                  //     var from = Memory.LocalPlayer.Skeleton.Bones[eft_dma_radar.Common.Unity.Bones.HumanHead].Position;
                  //     foreach (var player in Memory.Players)
                  //     {
                  //         var to = player.Skeleton.Bones[eft_dma_radar.Common.Unity.Bones.HumanHead].Position;
                  //         bool visible = PhysXManager.IsVisible(from, to);
                  //         if (visible)
                  //             NotificationsShared.Info($"Player {player.Name} is visible from the local player's head.");
                  //         else
                  //             NotificationsShared.Info($"Player {player.Name} is NOT visible from the local player's head. {to}");
                  //     }
                  //     NotificationsShared.Info("Test action executed!");
                  // }
                  // catch (Exception ex)
                  // {
                  //     NotificationsShared.Error($"Error executing test action: {ex.Message}");
                  // }
                    //break;
                #endregion

                #region Loot
                case nameof(HotkeyConfig.ShowLoot):
                    Config.ProcessLoot = isActive;
                    mainWindow.LootSettingsControl.chkProcessLoot.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ShowWishlistLoot):
                    Config.LootWishlist = isActive;
                    mainWindow.LootSettingsControl.chkShowLootWishlist.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ShowMeds):
                    LootFilterControl.ShowMeds = isActive;
                    mainWindow.LootSettingsControl.UpdateSpecificLootFilterOption("Show Meds", isActive);
                    break;
                case nameof(HotkeyConfig.ShowFood):
                    LootFilterControl.ShowFood = isActive;
                    mainWindow.LootSettingsControl.UpdateSpecificLootFilterOption("Show Food", isActive);
                    break;
                case nameof(HotkeyConfig.ShowWeapons):
                    LootFilterControl.ShowWeapons = isActive;
                    mainWindow.LootSettingsControl.UpdateSpecificLootFilterOption("Show Weapons", isActive);
                    break;
                case nameof(HotkeyConfig.ShowBackpacks):
                    LootFilterControl.ShowBackpacks = isActive;
                    mainWindow.LootSettingsControl.UpdateSpecificLootFilterOption("Show Backpacks", isActive);
                    break;
                case nameof(HotkeyConfig.ShowContainers):
                    Config.Containers.Show = isActive;
                    mainWindow.LootSettingsControl.chkStaticContainers.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.FuserImportantCorpseLoot):
                    Config.ESP.EntityTypeESPSettings.GetSettings("Corpse").ShowImportantLoot = isActive;
                    mainWindow.ESPControl.chkShowImportantCorpseLoot.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.FuserImportantPlayerLoot):
                    mainWindow.ESPControl.chkShowImportantPlayerLoot.IsChecked = isActive;
                    foreach (var setting in Config.ESP.PlayerTypeESPSettings.Settings)
                        setting.Value.ShowImportantLoot = isActive;
                    break;
                #endregion

                #region Fuser ESP
                case nameof(HotkeyConfig.ToggleFuserESP):
                    ESPForm.ShowESP = isActive;
                    break;
                case nameof(HotkeyConfig.MiniRadarZoomIn):
                    ExecuteContinuousAction(actionKey, () => ESPForm.Window?.ZoomIn(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.MiniRadarZoomOut):
                    ExecuteContinuousAction(actionKey, () => ESPForm.Window?.ZoomOut(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.FuserQuestInfo):
                    Config.ESP.ShowQuestInfoWidget = isActive;
                    mainWindow.ESPControl.UpdateSpecificWidgetOption("Quest Info Widget", isActive);
                    break;
                case nameof(HotkeyConfig.ImportantCorpseLoot):
                    Config.EntityTypeSettings.GetSettings("Corpse").ShowImportantLoot = isActive;
                    chkShowImportantCorpseLoot.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ImportantPlayerLoot):
                    chkShowImportantPlayerLoot.IsChecked = isActive;
                    foreach (var setting in Config.PlayerTypeSettings.Settings)
                        setting.Value.ShowImportantLoot = isActive;
                    break;
                #endregion

                #region Memory Writes
                // Global
                case nameof(HotkeyConfig.ToggleRageMode):
                    Config.MemWrites.RageMode = isActive;
                    mainWindow.MemoryWritingControl.chkRageMode.IsChecked = isActive;
                    break;
                // Aimbot
                case nameof(HotkeyConfig.ToggleAimbot):
                    Config.MemWrites.Aimbot.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkEnableAimbot.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.EngageAimbot):
                    XMLogging.WriteLine($"[Hotkeys] ExecuteHotkeyAction: EngageAimbot = {isActive}");
                    Aimbot.Engaged = isActive;
                    break;
                case nameof(HotkeyConfig.EngageLTW):
                    LootThroughWalls.ZoomEngaged = isActive;
                    break;                    
                case nameof(HotkeyConfig.EngageTeammate):
                    XMLogging.WriteLine($"[Hotkeys] ExecuteHotkeyAction: EngageTeammate = {isActive}");
                    TeammatesWorker.Engaged = isActive;
                    break;
                case nameof(HotkeyConfig.ToggleAimbotMode):
                    if (isActive)
                    {
                        Config.MemWrites.Aimbot.TargetingMode = Config.MemWrites.Aimbot.TargetingMode == AimbotTargetingMode.FOV
                            ? AimbotTargetingMode.CQB
                            : AimbotTargetingMode.FOV;
                    }
                    break;
                case nameof(HotkeyConfig.AimbotBone):
                    if (isActive)
                        mainWindow.MemoryWritingControl.ToggleAimbotBone();
                    break;
                case nameof(HotkeyConfig.SafeLock):
                    Config.MemWrites.Aimbot.SilentAim.SafeLock = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Safe Lock", isActive);
                    break;
                case nameof(HotkeyConfig.RandomBone):
                    Config.MemWrites.Aimbot.RandomBone.Enabled = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Random Bone", isActive);
                    break;
                case nameof(HotkeyConfig.AutoBone):
                    Config.MemWrites.Aimbot.SilentAim.AutoBone = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Auto Bone", isActive);
                    break;
                case nameof(HotkeyConfig.HeadshotAI):
                    Config.MemWrites.Aimbot.HeadshotAI = isActive;
                    mainWindow.MemoryWritingControl.UpdateSpecificAimbotOption("Headshot AI", isActive);
                    break;
                // Weapons
                case nameof(HotkeyConfig.FastWeaponOps):
                    Config.MemWrites.FastWeaponOps = isActive;
                    mainWindow.MemoryWritingControl.chkFastWeaponOps.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.NoRecoil):
                    Config.MemWrites.NoRecoil = isActive;
                    mainWindow.MemoryWritingControl.chkNoRecoil.IsChecked = isActive;
                    break;
                // Movement
                case nameof(HotkeyConfig.WideLean):
                    Config.MemWrites.WideLean.Enabled = isActive;
                    mainWindow.MemoryWritingControl.chkWideLean.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.WideLeanUp):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Up : WideLean.EWideLeanDirection.Off);
                    break;
                case nameof(HotkeyConfig.WideLeanLeft):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Left : WideLean.EWideLeanDirection.Off);
                    break;
                case nameof(HotkeyConfig.WideLeanRight):
                    SetWideLeanDirection(isActive ? WideLean.EWideLeanDirection.Right : WideLean.EWideLeanDirection.Off);
                    break;
                // Camera
                case nameof(HotkeyConfig.NoVisor):
                    Config.MemWrites.NoVisor = isActive;
                    mainWindow.MemoryWritingControl.chkNoVisor.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.NightVision):
                    Config.MemWrites.NightVision = isActive;
                    mainWindow.MemoryWritingControl.chkNightVision.IsChecked = isActive;
                    break;
                case nameof(HotkeyConfig.ThermalVision):
                    Config.MemWrites.ThermalVision = isActive;
                    mainWindow.MemoryWritingControl.chkThermalVision.IsChecked = isActive;
                    break;
                #endregion

                #region General Settings
                case nameof(HotkeyConfig.AimviewWidget):
                    Config.AimviewWidgetEnabled = isActive;
                    UpdateSpecificWidgetOption("Aimview Widget", isActive);
                    break;
                case nameof(HotkeyConfig.DebugWidget):
                    Config.ShowDebugWidget = isActive;
                    UpdateSpecificWidgetOption("Debug Widget", isActive);
                    break;
                case nameof(HotkeyConfig.PlayerInfoWidget):
                    Config.ShowInfoTab = isActive;
                    UpdateSpecificWidgetOption("Player Info Widget", isActive);
                    break;
                case nameof(HotkeyConfig.LootInfoWidget):
                    Config.ShowLootInfoWidget = isActive;
                    UpdateSpecificWidgetOption("Loot Info Widget", isActive);
                    break;
                case nameof(HotkeyConfig.QuestInfoWidget):
                    Config.ShowLootInfoWidget = isActive;
                    UpdateSpecificWidgetOption("Quest Info Widget", isActive);
                    break;
                //case nameof(HotkeyConfig.ConnectGroups):
                //    Config.ConnectGroups = isActive;
                //    UpdateSpecificGeneralOption("Connect Groups", isActive);
                //    break;
                case nameof(HotkeyConfig.MaskNames):
                    Config.MaskNames = isActive;
                    UpdateSpecificGeneralOption("Mask Names", isActive);
                    break;
                case nameof(HotkeyConfig.PlayersOnTop):
                    Config.PlayersOnTop = isActive;
                    UpdateSpecificGeneralOption("Players on Top", isActive);
                    break;
                case nameof(HotkeyConfig.ZoomIn):
                    ExecuteContinuousAction(actionKey, () => mainWindow.ZoomIn(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.ZoomOut):
                    ExecuteContinuousAction(actionKey, () => mainWindow.ZoomOut(HK_ZoomAmt));
                    break;
                case nameof(HotkeyConfig.BattleMode):
                    Config.BattleMode = isActive;
                    break;
                case nameof(HotkeyConfig.QuestHelper):
                    Config.QuestHelper.Enabled = isActive;
                    chkQuestHelper.IsChecked = isActive;
                    break;
                #endregion

                default:
                    XMLogging.WriteLine($"[Hotkey] No action defined for: {actionKey}");
                    break;
            }
        }

        private void ExecuteContinuousAction(string actionKey, Action action)
        {
            action();

            if (IsContinuousAction(actionKey))
            {
                var config = Config.HotKeys;
                var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                if (prop?.GetValue(config) is HotkeyEntry entry)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        if (InputManager.IsKeyDown(entry.Key))
                            Dispatcher.Invoke(() => ExecuteContinuousAction(actionKey, action));
                    });
                }
            }
        }

        private void SetWideLeanDirection(WideLean.EWideLeanDirection dir)
        {
            if (!Config.MemWrites.WideLean.Enabled)
            {
                WideLean.Direction = WideLean.EWideLeanDirection.Off;
                return;
            }

            WideLean.Direction = WideLean.Direction == dir ? WideLean.EWideLeanDirection.Off : dir;
        }

        private void RefreshHotkeyDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                hotkeyListView.Items.Refresh();
            });
        }

        #endregion

        #region Events
        private void btnAddHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (cboAction.SelectedValue is string actionKey &&
                cboAction.SelectedItem is HotkeyActionModel actionModel)
            {
                if (keyInputBox.SelectedKeyCode == -1)
                {
                    NotificationsShared.Error("Please select a key or mouse button first.");
                    return;
                }

                var keyName = keyInputBox.GetCurrentKeyName();
                var keyCode = keyInputBox.SelectedKeyCode;
                var type = rdbToggle.IsChecked == true ? "Toggle" : "OnKey";
                var existingAction = _hotkeyList.FirstOrDefault(h => h.Action == actionModel.Name);

                if (existingAction != null)
                {
                    _hotkeyList.Remove(existingAction);

                    var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                    if (prop?.GetValue(Config.HotKeys) is HotkeyEntry oldEntry)
                    {
                        oldEntry.Enabled = false;
                        oldEntry.Key = -1;
                    }
                }

                _hotkeyList.Add(new HotkeyDisplayModel
                {
                    Action = actionModel.Name,
                    Key = keyName,
                    Type = type
                });

                var configProp = typeof(HotkeyConfig).GetProperty(actionKey);
                if (configProp?.GetValue(Config.HotKeys) is HotkeyEntry entry)
                {
                    entry.Enabled = true;
                    entry.Key = keyCode;
                    entry.Mode = type == "Toggle" ? HotkeyMode.Toggle : HotkeyMode.OnKey;
                    Config.Save();

                    RegisterHotkeyHandlers();
                }

                keyInputBox.ClearInput();
                RefreshHotkeyDisplay();
            }
        }

        private void btnRemoveHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (hotkeyListView.SelectedItem is HotkeyDisplayModel selected)
            {
                _hotkeyList.Remove(selected);

                var actionKey = AvailableHotkeyActions.FirstOrDefault(a => a.Name == selected.Action)?.Key;

                if (!string.IsNullOrEmpty(actionKey))
                {
                    var prop = typeof(HotkeyConfig).GetProperty(actionKey);
                    if (prop?.GetValue(Config.HotKeys) is HotkeyEntry entry)
                    {
                        entry.Enabled = false;
                        entry.Key = -1;
                        Config.Save();

                        _toggleStates.Remove(actionKey);
                    }
                }

                RegisterHotkeyHandlers();
                RefreshHotkeyDisplay();
            }
        }
        #endregion
        #endregion

        #region ConfigTab
        private bool _isRefreshingConfigList = false;
        private bool _ignoreConfigSelectionChanged = false;
        private async Task InitializeConfigTab()
        {
            RefreshConfigList();
            txtCurrentConfig.Text = Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName);

            btnSaveConfig.Click += BtnCreateConfig_Click;
            btnDeleteConfig.Click += BtnDeleteConfig_Click;
            btnResetConfig.Click += BtnResetConfig_Click;
            btnRefreshConfigs.Click += BtnRefreshConfigs_Click;
            btnImportClipboard.Click += BtnImportClipboard_Click;
            btnExportClipboard.Click += BtnExportClipboard_Click;

            cboConfigs.SelectionChanged += CboConfigs_SelectionChanged;
        }

        private async void CboConfigs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ignoreConfigSelectionChanged)
                return;

            if (cboConfigs.SelectedIndex >= 0)
                BtnLoadConfig_Click(null, null);

            await Dispatcher.InvokeAsync(() =>
            {
                mainWindow.UpdateWindowTitle(Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName));
            });
        }
        private void RefreshConfigList()
        {
            _isRefreshingConfigList = true;
        
            try
            {
                _ignoreConfigSelectionChanged = true;
                cboConfigs.Items.Clear();
        
                var configs = ConfigManager.GetAvailableConfigs()
                    .OrderBy(c => c.ConfigName)
                    .ToList();
        
                var currentConfigNameWithoutExt = Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName);
                var selectedIndex = -1;
        
                foreach (var config in configs)
                {
                    var displayName = Path.GetFileNameWithoutExtension(config.ConfigName);
                    cboConfigs.Items.Add(displayName);
        
                    if (displayName.Equals(currentConfigNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                        selectedIndex = cboConfigs.Items.Count - 1;
                }
        
                cboConfigs.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        
                txtCurrentConfig.Text = currentConfigNameWithoutExt;
            }
            finally
            {
                _ignoreConfigSelectionChanged = false;
                _isRefreshingConfigList = false;
            }
        }


        private async void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            if (cboConfigs.SelectedIndex < 0)
                return;

            var selectedConfigName = cboConfigs.SelectedItem.ToString();
            var configToLoad = selectedConfigName + ".json";

            var confirm = MessageBox.Show(
                $"WARNING: Loading configuration '{selectedConfigName}' will overwrite current settings.\nContinue?",
                "Confirm Load", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;
            
            try
            {
                ESPForm.CloseESP();

                ConfigManager.CurrentConfig.Save();
                
                var loaded = await Task.Run(() => ConfigManager.LoadConfig(configToLoad));

                if (loaded)
                {
                    await ApplyNewConfig();

                    txtCurrentConfig.Text = Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName);
                    mainWindow.UpdateWindowTitle(txtCurrentConfig.Text);
                    RefreshConfigList();

                    NotificationsShared.Success($"Loaded '{selectedConfigName}' successfully!");
                }
                else
                {
                    NotificationsShared.Error($"Failed to load '{selectedConfigName}'!");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"Error loading config: {ex}");
                NotificationsShared.Error($"Error loading config: {ex.Message}");
            }
        }

        public static async Task ApplyConfig()
        {
            GeneralSettingsControl GenSettings = new GeneralSettingsControl();
            await GenSettings.ApplyNewConfig();
        }

        private async Task ApplyNewConfig()
        {
            try
            {

                var mainWindow = MainWindow.Window;

                if (mainWindow == null)
                    return;

                Program.UpdateConfig(ConfigManager.CurrentConfig);

                await Dispatcher.InvokeAsync(() =>
                {
                    LoadGeneralSettings();
                    LoadColorSettings();
                    LoadHotkeySettings();
                    UpdateUIScale();
                });

                await Dispatcher.InvokeAsync(async () =>
                {
                    if (mainWindow.MemoryWritingControl != null)
                    {
                        MemWrites.Enabled = Config.MemWrites.MemWritesEnabled;
                        mainWindow.MemoryWritingControl.LoadSettings();
                        await Task.Delay(50);
                        mainWindow.MemoryWritingControl.FeatureInstanceCheck();
                    }

                    if (mainWindow.LootSettingsControl != null)
                    {
                        mainWindow.LootSettingsControl.LoadSettings();
                        await Task.Delay(50);
                        await Task.Run(() => RefreshContainerData());
                    }

                    if (mainWindow.ESPControl != null)
                    {
                        mainWindow.ESPControl.LoadSettings();
                        await Task.Delay(50);
                    }
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.RestorePanelPositions();
                    mainWindow.RestoreToolbarPosition();

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        mainWindow.EnsureAllPanelsInBounds();
                        if (mainWindow.customToolbar != null)
                            mainWindow.EnsurePanelInBounds(mainWindow.customToolbar, mainWindow.mainContentGrid);
                    };
                    timer.Start();
                });

                await Task.Run(() => RefreshQuestData());
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateFeatureInstances();
                });

                await Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.UpdateWindowTitle(Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName));
                });
                //ConfigManager.CurrentConfig.Save();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Error applying new config: {ex}");
                MessageBox.Show($"Error applying configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCreateConfig_Click(object sender, RoutedEventArgs e)
        {
            var newConfigName = txtNewConfigName.Text.Trim();
            if (string.IsNullOrWhiteSpace(newConfigName))
            {
                MessageBox.Show("Please enter a name for the new config.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            newConfigName = Path.GetFileNameWithoutExtension(newConfigName);

            try
            {
                ConfigManager.CurrentConfig.Save();

                Config.ConfigName = newConfigName;
                Config.Filename = newConfigName + ".json";
                XMLogging.WriteLine($"[Config] Creating new config: {Config.ConfigName}");

                var saved = ConfigManager.SaveAsNewConfig(Config.Filename);

                if (saved)
                {
                    Config.Filename = Path.GetFileNameWithoutExtension(ConfigManager.CurrentConfigName);
                    txtCurrentConfig.Text = newConfigName;
                    RefreshConfigList();

                    _ignoreConfigSelectionChanged = true;
                    try
                    {
                        for (int i = 0; i < cboConfigs.Items.Count; i++)
                        {
                            if (cboConfigs.Items[i].ToString().Equals(newConfigName, StringComparison.OrdinalIgnoreCase))
                            {
                                cboConfigs.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    finally
                    {
                        _ignoreConfigSelectionChanged = false;
                    }

                    NotificationsShared.Success($"Config '{newConfigName}' created and saved!");
                }
                else
                {
                    NotificationsShared.Error($"Failed to save '{newConfigName}'!");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Error creating new config: {ex}");
                NotificationsShared.Error($"Error creating new config: {ex.Message}");
            }
        }


        private void BtnDeleteConfig_Click(object sender, RoutedEventArgs e)
        {
            if (cboConfigs.SelectedIndex <= 0)
                return;

            var configToDelete = cboConfigs.SelectedItem.ToString();

            if (cboConfigs.SelectedIndex > 0 && !configToDelete.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                configToDelete += ".json";

            var result = MessageBox.Show($"Are you sure you want to delete config '{configToDelete}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (ConfigManager.DeleteConfig(configToDelete))
                {
                    MessageBox.Show($"Config '{Path.GetFileNameWithoutExtension(configToDelete)}' deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshConfigList();
                }
                else
                {
                    MessageBox.Show($"Failed to delete config '{Path.GetFileNameWithoutExtension(configToDelete)}'.\n" + "Check logs for more details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnResetConfig_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.ResetToDefault();
            MessageBox.Show("Default config restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshConfigList();
        }

        private void BtnRefreshConfigs_Click(object sender, RoutedEventArgs e)
        {
            RefreshConfigList();
        }

        private void BtnExportClipboard_Click(object sender, RoutedEventArgs e)
        {
            ExportConfigToClipboard();
        }   

        private void ExportConfigToClipboard()
        {
            try
            {
                if (Config == null)
                {
                    NotificationsShared.Warning("[Config] No configuration available to export.");
                    return;
                }

                var configForExport = JsonSerializer.Deserialize<Config>(JsonSerializer.Serialize(Config));
                configForExport.Cache = null;
                configForExport.WebRadar = null;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var jsonData = JsonSerializer.Serialize(configForExport, options);
                Clipboard.SetText(jsonData);

                NotificationsShared.Success("[Config] Configuration exported to clipboard successfully! (Cache and WebRadar settings excluded)");
                XMLogging.WriteLine("[Config] Configuration exported to clipboard (excluding Cache and WebRadar)");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Export error: {ex}");
                NotificationsShared.Error($"[Config] Export error: {ex.Message}");
            }
        }

        private async void BtnImportClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    NotificationsShared.Warning("[Config] Clipboard does not contain text data.");
                    return;
                }

                var clipboardText = Clipboard.GetText();

                var confirm = MessageBox.Show(
                    "WARNING: Importing a configuration will replace current settings including:\n\n" +
                    "ï¿? General settings & UI preferences\n" +
                    "ï¿? Player/Entity display settings\n" +
                    "ï¿? Color configurations\n" +
                    "ï¿? Hotkey assignments\n" +
                    "ï¿? ESP configurations\n" +
                    "ï¿? Panel and toolbar positions\n" +
                    "ï¿? Memory writing settings\n" +
                    "ï¿? Loot settings\n" +
                    "ï¿? Quest helper settings\n" +
                    "ï¿? Container settings\n\n" +
                    "NOTE: Cache data will not be preserved.\n\n" +
                    "This action cannot be undone. Continue?",
                    "Import Configuration Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                    return;

                btnImportClipboard.IsEnabled = false;

                Config importedConfig = null;

                await Task.Run(() =>
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    importedConfig = JsonSerializer.Deserialize<Config>(clipboardText, options);
                });

                if (importedConfig == null)
                {
                    NotificationsShared.Error("[Config] Clipboard data is not a valid configuration.");
                    return;
                }

                var baseName = importedConfig.ConfigName?.Trim();
                if (string.IsNullOrWhiteSpace(baseName))
                    baseName = "ImportedConfig";
                else
                    baseName = Path.GetFileNameWithoutExtension(baseName);

                var finalName = baseName;
                var counter = 1;

                while (File.Exists(Path.Combine(ConfigManager.CustomConfigDirectory, finalName + ".json")))
                {
                    finalName = $"{baseName}-{counter}";
                    counter++;
                }

                importedConfig.ConfigName = finalName;

                var saved = await Task.Run(() => ConfigManager.SaveAsNewConfig(finalName + ".json"));

                if (saved)
                {
                    Config.ConfigName = finalName + ".json";
                    txtCurrentConfig.Text = finalName;

                    NotificationsShared.Success($"Configuration imported and saved as '{finalName}'!");
                    RefreshConfigList();
                }
                else
                {
                    NotificationsShared.Error("[Config] Failed to save imported configuration.");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Config] Import error: {ex}");
                NotificationsShared.Error($"[Config] Import error: {ex.Message}");
            }
            finally
            {
                btnImportClipboard.IsEnabled = true;
            }
        }          
        #endregion
    }
}