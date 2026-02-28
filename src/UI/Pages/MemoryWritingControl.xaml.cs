using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.LowLevel;

using eft_dma_radar.Tarkov;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.UI.Controls;
using eft_dma_radar.UI.Misc;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using HandyControl.Controls;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.Features.MemoryWrites.Aimbot;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = HandyControl.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for MemoryWritingSettingsControl.xaml
    /// </summary>
    public partial class MemoryWritingControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second

        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private static Config Config => Program.Config;

        private bool _isLoadingAimbotOptions = false;
        private readonly string[] _availableAimbotOptions = new string[]
        {
            "Safe Lock",
            "Disable Re-Lock",
            "Auto Bone",
            "Headshot AI",
            "Random Bone"
        };
        #endregion

        public MemoryWritingControl()
        {
            InitializeComponent();
            TooltipManager.AssignMemoryWritingTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("MemoryWriting");
                ExpanderManager.Instance.RegisterExpanders(this, "MemoryWriting",
                    expGlobalSettings,
                    expAimbotSettings,
                    expWeapons,
                    expMovement,
                    expCamera,
                    expMisc);

                try
                {
                    await PanelCoordinator.Instance.WaitForAllPanelsAsync();

                    InitializeControlEvents();
                    LoadSettings();
                }
                catch (TimeoutException ex)
                {
                    XMLogging.WriteLine($"[PANELS] {ex.Message}");
                }
            };
        }

        #region Memory Writing Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterSettingsEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        }

        public void LoadSettings()
        {
            Dispatcher.Invoke(() =>
            {
                LoadAllSettings();
            });
        }
        #endregion

        #region Events
        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
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
        #endregion
        #endregion

        #region Memory Writing Settings
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            // Global Settings
            chkMasterSwitch.Checked += MemWritingCheckbox_Checked;
            chkMasterSwitch.Unchecked += MemWritingCheckbox_Checked;
            chkRageMode.Checked += MemWritingCheckbox_Checked;
            chkRageMode.Unchecked += MemWritingCheckbox_Checked;
            btnAntiAFK.Click += btnAntiAFK_Click;
            btnTest.Click += btnTest_Click;

            // Aimbot Settings
            chkEnableAimbot.Checked += MemWritingCheckbox_Checked;
            chkEnableAimbot.Unchecked += MemWritingCheckbox_Checked;
            rdbFOV.Checked += MemWritingRadioButton_Checked;
            rdbCQB.Checked += MemWritingRadioButton_Checked;
            cboTargetBone.SelectionChanged += cboTargetBone_SelectionChanged;
            sldrAimbotFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotDistance.ValueChanged += MemWritingSlider_ValueChanged;
            ccbAimbotOptions.SelectionChanged += aimbotOptionsCheckComboBox_SelectionChanged;
            sldrAimbotRNGHead.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGTorso.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGArms.ValueChanged += MemWritingSlider_ValueChanged;
            sldrAimbotRNGLegs.ValueChanged += MemWritingSlider_ValueChanged;

            // Weapons
            chkNoWeaponMalfunctions.Checked += MemWritingCheckbox_Checked;
            chkNoWeaponMalfunctions.Unchecked += MemWritingCheckbox_Checked;
            chkMagDrills.Checked += MemWritingCheckbox_Checked;
            chkMagDrills.Unchecked += MemWritingCheckbox_Checked;
            chkFastWeaponOps.Checked += MemWritingCheckbox_Checked;
            chkFastWeaponOps.Unchecked += MemWritingCheckbox_Checked;
            chkDisableWeaponCollision.Checked += MemWritingCheckbox_Checked;
            chkDisableWeaponCollision.Unchecked += MemWritingCheckbox_Checked;
            chkRemoveableAttachments.Checked += MemWritingCheckbox_Checked;
            chkRemoveableAttachments.Unchecked += MemWritingCheckbox_Checked;
            chkNoRecoil.Checked += MemWritingCheckbox_Checked;
            chkNoRecoil.Unchecked += MemWritingCheckbox_Checked;
            btnNoRecoilConfig.Click += MemWritingButton_Clicked;
            sldrNoRecoilAmt.ValueChanged += MemWritingSlider_ValueChanged;
            sldrNoSwayAmt.ValueChanged += MemWritingSlider_ValueChanged;

            // Movement
            chkInfiniteStamina.Checked += MemWritingCheckbox_Checked;
            chkInfiniteStamina.Unchecked += MemWritingCheckbox_Checked;
            chkMoveSpeed.Checked += MemWritingCheckbox_Checked;
            chkMoveSpeed.Unchecked += MemWritingCheckbox_Checked;
            btnMoveSpeedConfig.Click += MemWritingButton_Clicked;
            sldrMoveSpeedMultiplier.ValueChanged += MemWritingSlider_ValueChanged;
            chkFastDuck.Checked += MemWritingCheckbox_Checked;
            chkFastDuck.Unchecked += MemWritingCheckbox_Checked;
            chkMuleMode.Checked += MemWritingCheckbox_Checked;
            chkMuleMode.Unchecked += MemWritingCheckbox_Checked;
            chkNoInertia.Checked += MemWritingCheckbox_Checked;
            chkNoInertia.Unchecked += MemWritingCheckbox_Checked;
            chkWideLean.Checked += MemWritingCheckbox_Checked;
            chkWideLean.Unchecked += MemWritingCheckbox_Checked;
            btnWideLeanConfig.Click += MemWritingButton_Clicked;
            sldrLeanAmt.ValueChanged += MemWritingSlider_ValueChanged;
            chkLongJump.Checked += MemWritingCheckbox_Checked;
            chkLongJump.Unchecked += MemWritingCheckbox_Checked;
            btnLongJumpConfig.Click += MemWritingButton_Clicked;
            sldrLongJumpMultiplier.ValueChanged += MemWritingSlider_ValueChanged;

            // World
            chkTimeOfDay.Checked += MemWritingCheckbox_Checked;
            chkTimeOfDay.Unchecked += MemWritingCheckbox_Checked;
            btnTimeOfDayConfig.Click += MemWritingButton_Clicked;
            sldrTimeOfDayHour.ValueChanged += MemWritingSlider_ValueChanged;
            chkFullBright.Checked += MemWritingCheckbox_Checked;
            chkFullBright.Unchecked += MemWritingCheckbox_Checked;
            btnFullBrightConfig.Click += MemWritingButton_Clicked;
            sldrFullBrightIntensity.ValueChanged += MemWritingSlider_ValueChanged;

            chkLTW.Checked += MemWritingCheckbox_Checked;
            chkLTW.Unchecked += MemWritingCheckbox_Checked;
            btnLTWConfig.Click += MemWritingButton_Clicked;
            sldrLTWZoom.ValueChanged += MemWritingSlider_ValueChanged;
            chkExtendedReach.Checked += MemWritingCheckbox_Checked;
            chkExtendedReach.Unchecked += MemWritingCheckbox_Checked;
            btnExtendedReachConfig.Click += MemWritingButton_Clicked;
            sldrExtendedReachDistance.ValueChanged += MemWritingSlider_ValueChanged;
            // Camera
            chkNoVisor.Checked += MemWritingCheckbox_Checked;
            chkNoVisor.Unchecked += MemWritingCheckbox_Checked;
            chkNightVision.Checked += MemWritingCheckbox_Checked;
            chkNightVision.Unchecked += MemWritingCheckbox_Checked;
            chkDisableFrostbite.Checked += MemWritingCheckbox_Checked;
            chkDisableFrostbite.Unchecked += MemWritingCheckbox_Checked;            
            chkThermalVision.Checked += MemWritingCheckbox_Checked;
            chkThermalVision.Unchecked += MemWritingCheckbox_Checked;
            chkThirdPerson.Checked += MemWritingCheckbox_Checked;
            chkThirdPerson.Unchecked += MemWritingCheckbox_Checked;
            chkOwlMode.Checked += MemWritingCheckbox_Checked;
            chkOwlMode.Unchecked += MemWritingCheckbox_Checked;
           // chkDisableScreenEffects.Checked += MemWritingCheckbox_Checked;
           // chkDisableScreenEffects.Unchecked += MemWritingCheckbox_Checked;
            chkDisableShadows.Checked += MemWritingCheckbox_Checked;
            chkDisableShadows.Unchecked += MemWritingCheckbox_Checked;
            chkDisableGrass.Checked += MemWritingCheckbox_Checked;
            chkDisableGrass.Unchecked += MemWritingCheckbox_Checked;
            chkClearWeather.Checked += MemWritingCheckbox_Checked;
            chkClearWeather.Unchecked += MemWritingCheckbox_Checked;
            chkDisableHeadBobbing.Checked += MemWritingCheckbox_Checked;
            chkDisableHeadBobbing.Unchecked += MemWritingCheckbox_Checked;
            chkFOVChanger.Checked += MemWritingCheckbox_Checked;
            chkFOVChanger.Unchecked += MemWritingCheckbox_Checked;
            btnFOVConfig.Click += MemWritingButton_Clicked;
            sldrFOVBase.ValueChanged += MemWritingSlider_ValueChanged;
            sldrADSFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrTPPFOV.ValueChanged += MemWritingSlider_ValueChanged;
            sldrZoomFOV.ValueChanged += MemWritingSlider_ValueChanged;

            // Misc
            chkInstantPlant.Checked += MemWritingCheckbox_Checked;
            chkInstantPlant.Unchecked += MemWritingCheckbox_Checked;
            chkMedPanel.Checked += MemWritingCheckbox_Checked;
            chkMedPanel.Unchecked += MemWritingCheckbox_Checked;
            chkDisableInventoryBlur.Checked += MemWritingCheckbox_Checked;
            chkDisableInventoryBlur.Unchecked += MemWritingCheckbox_Checked;
            chkBigHeads.Checked += MemWritingCheckbox_Checked;
            chkBigHeads.Unchecked += MemWritingCheckbox_Checked;
            btnBigHeadsConfig.Click += MemWritingButton_Clicked;
            sldrBigHeadScale.ValueChanged += MemWritingSlider_ValueChanged;
            chkHideRaidCode.Checked += MemWritingCheckbox_Checked;
            chkHideRaidCode.Unchecked += MemWritingCheckbox_Checked;
        }

        private void LoadAllSettings()
        {
            var cfg = MemWrites.Config;

            // Global Settings
            chkMasterSwitch.IsChecked = cfg.MemWritesEnabled;
            //chkAdvancedWrites.IsChecked = cfg.AdvancedMemWrites;
            //chkAntiPage.IsChecked = cfg.AntiPage;

            // Aimbot Settings
            LoadAimbotOptions();

            // Weapon
            chkNoWeaponMalfunctions.IsChecked = cfg.NoWeaponMalfunctions;
            chkMagDrills.IsChecked = cfg.FastLoadUnload;
            chkFastWeaponOps.IsChecked = cfg.FastWeaponOps;
            chkDisableWeaponCollision.IsChecked = cfg.DisableWeaponCollision;
            //chkRemoveableAttachments.IsChecked = cfg.RemoveableAttachments;
            chkNoRecoil.IsChecked = cfg.NoRecoil;
            sldrNoRecoilAmt.Value = cfg.NoRecoilAmount;
            sldrNoSwayAmt.Value = cfg.NoSwayAmount;

            // Movement
            chkInfiniteStamina.IsChecked = cfg.InfStamina;
            chkMoveSpeed.IsChecked = cfg.MoveSpeed.Enabled;
            sldrMoveSpeedMultiplier.Value = cfg.MoveSpeed.Multiplier;
            chkFastDuck.IsChecked = cfg.FastDuck;
            chkMuleMode.IsChecked = cfg.MuleMode;
            chkNoInertia.IsChecked = cfg.NoInertia;
            chkWideLean.IsChecked = cfg.WideLean.Enabled;
            sldrLeanAmt.Value = cfg.WideLean.Amount;
            chkLongJump.IsChecked = cfg.LongJump.Enabled;
            sldrLongJumpMultiplier.Value = cfg.LongJump.Multiplier;

            // World
            //chkDisableShadows.IsChecked = cfg.DisableShadows;
            chkDisableGrass.IsChecked = cfg.DisableGrass;
            chkClearWeather.IsChecked = cfg.ClearWeather;
            chkTimeOfDay.IsChecked = cfg.TimeOfDay.Enabled;
            sldrTimeOfDayHour.Value = cfg.TimeOfDay.Hour;
            chkFullBright.IsChecked = cfg.FullBright.Enabled;
            sldrFullBrightIntensity.Value = cfg.FullBright.Intensity;
            chkLTW.IsChecked = cfg.LootThroughWalls.Enabled;
            //chkSilentLoot.IsChecked = cfg.SilentLoot.Enabled;
            sldrLTWZoom.Value = cfg.LootThroughWalls.ZoomAmount;
            //sldrSilentLootDistance.Value = cfg.SilentLoot.Distance;
            //sldrSilentLootMaxDistance.Value = cfg.SilentLoot.MaxDistance;
            chkExtendedReach.IsChecked = cfg.ExtendedReach.Enabled;
            sldrExtendedReachDistance.Value = cfg.ExtendedReach.Distance;

            // Camera
            chkNoVisor.IsChecked = cfg.NoVisor;
            chkNightVision.IsChecked = cfg.NightVision;
            chkThermalVision.IsChecked = cfg.ThermalVision;
            chkThirdPerson.IsChecked = cfg.ThirdPerson;
            chkDisableFrostbite.IsChecked = cfg.DisableFrostbite;
            chkOwlMode.IsChecked = cfg.OwlMode;
            //chkDisableScreenEffects.IsChecked = cfg.DisableScreenEffects;
            chkDisableHeadBobbing.IsChecked = cfg.DisableHeadBobbing;
            //chkFOVChanger.IsChecked = cfg.FOV.Enabled;
            //sldrFOVBase.Value = cfg.FOV.Base;
            //sldrADSFOV.Value = cfg.FOV.ADS;
            //sldrTPPFOV.Value = cfg.FOV.ThirdPerson;
            //sldrZoomFOV.Value = cfg.FOV.InstantZoom;

            // Misc
            //chkStreamerMode.IsChecked = cfg.StreamerMode;
            chkHideRaidCode.IsChecked = cfg.HideRaidCode;
            chkInstantPlant.IsChecked = cfg.InstantPlant;
            chkMedPanel.IsChecked = cfg.MedPanel;
            chkDisableInventoryBlur.IsChecked = cfg.DisableInventoryBlur;
            //chkVisCheck.IsChecked = cfg.VisCheck.Enabled;
            //sldrVisCheckFarDistance.Value = cfg.VisCheck.FarDist;
            //sldrVisCheckMidDistance.Value = cfg.VisCheck.MidDist;
            //sldrVisCheckLowDistance.Value = cfg.VisCheck.LowDist;
            //chkIgnoreAi.IsChecked = cfg.VisCheck.IgnoreAi;
            chkBigHeads.IsChecked = cfg.BigHead.Enabled;
            sldrBigHeadScale.Value = cfg.BigHead.Scale;

            ToggleMemWritingControls();
            FeatureInstanceCheck();
        }

        private void LoadAimbotOptions()
        {
            _isLoadingAimbotOptions = true;

            try
            {
                var cfg = MemWrites.Config;
                var aimbotConfig = cfg.Aimbot;
                chkEnableAimbot.IsChecked = aimbotConfig.Enabled;
                var rdb = (aimbotConfig.TargetingMode == AimbotTargetingMode.FOV) ? rdbFOV : rdbCQB;
                rdb.IsChecked = true;
                cboTargetBone.SelectedIndex = cfg.Aimbot.Bone switch
                {
                    Bones.HumanHead => 0,
                    Bones.HumanNeck => 1,
                    Bones.HumanSpine3 => 2,
                    Bones.HumanPelvis => 3,
                    Bones.Legs => 4,
                    _ => 0
                };

                sldrAimbotFOV.Value = aimbotConfig.FOV;
                sldrAimbotDistance.Value = aimbotConfig.Distance;
                sldrAimbotRNGHead.Value = aimbotConfig.RandomBone.HeadPercent;
                sldrAimbotRNGTorso.Value = aimbotConfig.RandomBone.TorsoPercent;
                sldrAimbotRNGArms.Value = aimbotConfig.RandomBone.ArmsPercent;
                sldrAimbotRNGLegs.Value = aimbotConfig.RandomBone.LegsPercent;

                ccbAimbotOptions.Items.Clear();
                foreach (var option in _availableAimbotOptions)
                {
                    ccbAimbotOptions.Items.Add(new CheckComboBoxItem { Content = option });
                }
            }
            finally
            {
                _isLoadingAimbotOptions = false;
            }

            UpdateAimbotOptionSelections();
        }

        private void UpdateAimbotOptionSelections()
        {
            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Safe Lock"] = MemWrites.Config.Aimbot.SilentAim.SafeLock,
                ["Disable Re-Lock"] = MemWrites.Config.Aimbot.DisableReLock,
                ["Auto Bone"] = MemWrites.Config.Aimbot.SilentAim.AutoBone,
                ["Headshot AI"] = MemWrites.Config.Aimbot.HeadshotAI,
                ["Random Bone"] = MemWrites.Config.Aimbot.RandomBone.Enabled
            };

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    item.IsSelected = shouldBeSelected;
            }
        }

        private void ToggleMemWritingControls()
        {
            var memWritingEnabled = MemWrites.Enabled;

            // Global Settings
            chkRageMode.IsEnabled = memWritingEnabled;
            btnAntiAFK.IsEnabled = memWritingEnabled;
            btnTest.IsEnabled = memWritingEnabled;

            // Aimbot Settings
            ToggleAimbotControls();

            // Weapon
            chkNoWeaponMalfunctions.IsEnabled = memWritingEnabled;
            chkMagDrills.IsEnabled = memWritingEnabled;
            chkFastWeaponOps.IsEnabled = memWritingEnabled;
            chkDisableWeaponCollision.IsEnabled = memWritingEnabled;
            ToggleNoRecoilControls();

            // Movement
            chkInfiniteStamina.IsEnabled = memWritingEnabled;
            chkFastDuck.IsEnabled = memWritingEnabled;
            chkMuleMode.IsEnabled = memWritingEnabled;
            chkNoInertia.IsEnabled = memWritingEnabled;
            ToggleMoveSpeedControls();
            ToggleWideLeanControls();
            ToggleLongJumpControls();

            // World
            //chkDisableShadows.IsEnabled = memWritingEnabled;
            chkDisableGrass.IsEnabled = memWritingEnabled;
            chkClearWeather.IsEnabled = memWritingEnabled;
            ToggleFullBrightControls();
            ToggleTimeOfDayControls();
            ToggleLTWControls();
            ToggleSilentLootControls();
            ToggleExtendedReachControls();

            // Camera
            chkNoVisor.IsEnabled = memWritingEnabled;
            chkNightVision.IsEnabled = memWritingEnabled;
            chkThermalVision.IsEnabled = memWritingEnabled;
            chkThirdPerson.IsEnabled = memWritingEnabled;
            chkDisableFrostbite.IsEnabled = memWritingEnabled;
            chkOwlMode.IsEnabled = memWritingEnabled;
            //chkDisableHeadBobbing.IsEnabled = memWritingEnabled;
            ToggleFOVControls();

            // Misc
            chkInstantPlant.IsEnabled = memWritingEnabled;
            chkMedPanel.IsEnabled = memWritingEnabled;
            chkDisableInventoryBlur.IsEnabled = memWritingEnabled;
            ToggleBigHeadControls();
            ToggleVisCheckControls();

            ToggleAdvMemWritingControls();
            //chkHideRaidCode.IsEnabled = memWritingEnabled;
        }

        private void ToggleAimbotControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.Aimbot.Enabled;
            var rndBoneEnabled = MemWrites.Config.Aimbot.RandomBone.Enabled;

            chkEnableAimbot.IsEnabled = memWrites;
            rdbFOV.IsEnabled = enableControl;
            rdbCQB.IsEnabled = enableControl;
            sldrAimbotFOV.IsEnabled = enableControl;
            sldrAimbotDistance.IsEnabled = enableControl;
            ccbAimbotOptions.IsEnabled = enableControl;

            ToggleAimbotRandomBoneControls();
        }

        private void ToggleAimbotRandomBoneControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.Aimbot.Enabled;
            var rndBoneEnabled = MemWrites.Config.Aimbot.RandomBone.Enabled;

            cboTargetBone.IsEnabled = enableControl && !rndBoneEnabled;

            pnlBoneRNG.Visibility = (enableControl && rndBoneEnabled ? Visibility.Visible : Visibility.Collapsed);
            sldrAimbotRNGHead.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGTorso.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGArms.IsEnabled = enableControl && rndBoneEnabled;
            sldrAimbotRNGLegs.IsEnabled = enableControl && rndBoneEnabled;
        }

        private void ToggleNoRecoilControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.NoRecoil;

            chkNoRecoil.IsEnabled = memWrites;
            btnNoRecoilConfig.IsEnabled = enableControl;
            sldrNoRecoilAmt.IsEnabled = enableControl;
            sldrNoSwayAmt.IsEnabled = enableControl;

            if (!enableControl && pnlNoRecoil.Visibility == Visibility.Visible)
                pnlNoRecoil.Visibility = Visibility.Collapsed;
        }

        private void ToggleMoveSpeedControls()
        {
           var memWrites = MemWrites.Enabled;
           var enableControl = memWrites && MemWrites.Config.MoveSpeed.Enabled;
           chkMoveSpeed.IsEnabled = memWrites;
           btnMoveSpeedConfig.IsEnabled = enableControl;
           sldrMoveSpeedMultiplier.IsEnabled = enableControl;
           if (!enableControl && pnlMoveSpeed.Visibility == Visibility.Visible)
               pnlMoveSpeed.Visibility = Visibility.Collapsed;
        }

        private void ToggleWideLeanControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.WideLean.Enabled;

            chkWideLean.IsEnabled = memWrites;
            btnWideLeanConfig.IsEnabled = enableControl;
            sldrLeanAmt.IsEnabled = enableControl;

            if (!enableControl && pnlWideLean.Visibility == Visibility.Visible)
                pnlWideLean.Visibility = Visibility.Collapsed;
        }

        private void ToggleLongJumpControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.LongJump.Enabled;

            chkLongJump.IsEnabled = memWrites;
            btnLongJumpConfig.IsEnabled = enableControl;
            sldrLongJumpMultiplier.IsEnabled = enableControl;

            if (!enableControl && pnlLongJump.Visibility == Visibility.Visible)
                pnlLongJump.Visibility = Visibility.Collapsed;
        }

        private void ToggleBigHeadControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.BigHead.Enabled;

            chkBigHeads.IsEnabled = memWrites;
            btnBigHeadsConfig.IsEnabled = enableControl;
            sldrBigHeadScale.IsEnabled = enableControl;

            if (!enableControl && pnlBigHeads.Visibility == Visibility.Visible)
                pnlBigHeads.Visibility = Visibility.Collapsed;
        }
        private void ToggleVisCheckControls()
        {
            //var memWrites = MemWrites.Enabled;
            //var enableControl = memWrites && MemWrites.Config.VisCheck.Enabled;
//
            //btnVisCheckConfig.IsEnabled = enableControl;
            //sldrVisCheckLowDistance.IsEnabled = enableControl;
            //sldrVisCheckMidDistance.IsEnabled = enableControl;
            //sldrVisCheckFarDistance.IsEnabled = enableControl;
            //chkIgnoreAi.IsEnabled = enableControl;
//
            //if (!enableControl && pnlVisCheck.Visibility == Visibility.Visible)
            //    pnlVisCheck.Visibility = Visibility.Collapsed;
        }

        private void ToggleTimeOfDayControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.TimeOfDay.Enabled;

            chkTimeOfDay.IsEnabled = memWrites;
            btnTimeOfDayConfig.IsEnabled = enableControl;
            sldrTimeOfDayHour.IsEnabled = enableControl;

            if (!enableControl && pnlTimeOfDay.Visibility == Visibility.Visible)
                pnlTimeOfDay.Visibility = Visibility.Collapsed;
        }

        private void ToggleFullBrightControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.FullBright.Enabled;

            chkFullBright.IsEnabled = memWrites;
            btnFullBrightConfig.IsEnabled = enableControl;
            sldrFullBrightIntensity.IsEnabled = enableControl;

            if (!enableControl && pnlFullBright.Visibility == Visibility.Visible)
                pnlFullBright.Visibility = Visibility.Collapsed;
        }

        private void ToggleLTWControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.LootThroughWalls.Enabled;

            chkLTW.IsEnabled = memWrites;
            btnLTWConfig.IsEnabled = enableControl;
            sldrLTWZoom.IsEnabled = enableControl;

            if (!enableControl && pnlLTW.Visibility == Visibility.Visible)
                pnlLTW.Visibility = Visibility.Collapsed;
        }
        private void ToggleSilentLootControls()
        {
            //var memWrites = MemWrites.Enabled;
            //var enableControl = memWrites && MemWrites.Config.SilentLoot.Enabled;
//
            //chkSilentLoot.IsEnabled = memWrites;
            //btnSilentLootConfig.IsEnabled = enableControl;
            //sldrSilentLootDistance.IsEnabled = enableControl;
            //sldrSilentLootMaxDistance.IsEnabled = enableControl;
//
            //if (!enableControl && pnlSilentLoot.Visibility == Visibility.Visible)
            //    pnlSilentLoot.Visibility = Visibility.Collapsed;
        }

        private void ToggleExtendedReachControls()
        {
            var memWrites = MemWrites.Enabled;
            var enableControl = memWrites && MemWrites.Config.ExtendedReach.Enabled;

            chkExtendedReach.IsEnabled = memWrites;
            btnExtendedReachConfig.IsEnabled = enableControl;
            sldrExtendedReachDistance.IsEnabled = enableControl;

            if (!enableControl && pnlExtendedReach.Visibility == Visibility.Visible)
                pnlExtendedReach.Visibility = Visibility.Collapsed;
        }

        private void ToggleAdvMemWritingControls()
        {
            //var memWritingEnabled = MemWrites.Enabled;
            //var advMemWrites = MemWrites.AdvEnabled;
            //var enabled = (memWritingEnabled && advMemWrites);
//
            //// General Settings
            //chkAdvancedWrites.IsEnabled = memWritingEnabled;
            //chkAntiPage.IsEnabled = enabled;
//
            //// Chams
            //MainWindow.Window.ESPControl.UpdateChamsControls();
//
            //// Weapon
            //chkRemoveableAttachments.IsEnabled = enabled;
//
            //// World
            //chkDisableShadows.IsEnabled = enabled;
//
            //// Camera
            //chkDisableScreenEffects.IsEnabled = enabled;
            //chkFOVChanger.IsEnabled = enabled;
            //btnFOVConfig.IsEnabled = (enabled && MemWrites.Config.FOV.Enabled);
//
            //// Misc
            //chkStreamerMode.IsEnabled = enabled;
            //chkHideRaidCode.IsEnabled = enabled;
            //chkVisCheck.IsEnabled = enabled;
        }

        public void ToggleAimbotBone()
        {
            Dispatcher.Invoke(() =>
            {
                int maxIndex = cboTargetBone.Items.Count - 1;
                int currentIndex = cboTargetBone.SelectedIndex;
                int newIndex = currentIndex + 1;

                cboTargetBone.SelectedIndex = (newIndex > maxIndex) ? 0 : newIndex;
            });
        }

        private void ToggleFOVControls()
        {
            //var memWrites = MemWrites.Enabled;
            //var enableControl = memWrites && MemWrites.Config.FOV.Enabled;
//
            ////chkFOVChanger.IsEnabled = memWrites && MemWrites.Config.AdvancedMemWrites;
            ////btnFOVConfig.IsEnabled = enableControl && MemWrites.Config.AdvancedMemWrites;
            //sldrFOVBase.IsEnabled = enableControl;
            //sldrADSFOV.IsEnabled = enableControl;
            //sldrTPPFOV.IsEnabled = enableControl;
            //sldrZoomFOV.IsEnabled = enableControl;
//
            //if (!enableControl && pnlFOV.Visibility == Visibility.Visible)
            //    pnlFOV.Visibility = Visibility.Collapsed;
        }

        public void FeatureInstanceCheck()
        {
            var cfg = MemWrites.Config;

            //MemPatchFeature<FOVChanger>.Instance.Enabled = cfg.FOV.Enabled;
            MemWriteFeature<Aimbot>.Instance.Enabled = cfg.Aimbot.Enabled;
            MemWriteFeature<NoWepMalfPatch>.Instance.Enabled = cfg.NoWeaponMalfunctions;
            MemWriteFeature<FastLoadUnload>.Instance.Enabled = cfg.FastLoadUnload;
            MemWriteFeature<FastWeaponOps>.Instance.Enabled = cfg.FastWeaponOps;
            MemWriteFeature<DisableWeaponCollision>.Instance.Enabled = cfg.DisableWeaponCollision;
            //MemPatchFeature<RemoveableAttachments>.Instance.Enabled = cfg.RemoveableAttachments;
            MemWriteFeature<NoRecoil>.Instance.Enabled = cfg.NoRecoil;
            MemWriteFeature<InfStamina>.Instance.Enabled = cfg.InfStamina;
            MemWriteFeature<MoveSpeed>.Instance.Enabled = cfg.MoveSpeed.Enabled;
            MemWriteFeature<FastDuck>.Instance.Enabled = cfg.FastDuck;
            MemWriteFeature<MuleMode>.Instance.Enabled = cfg.MuleMode;
            MemWriteFeature<NoInertia>.Instance.Enabled = cfg.NoInertia;
            MemWriteFeature<TimeOfDay>.Instance.Enabled = cfg.TimeOfDay.Enabled;
            MemWriteFeature<LootThroughWalls>.Instance.Enabled = cfg.LootThroughWalls.Enabled;
            MemWriteFeature<ExtendedReach>.Instance.Enabled = cfg.ExtendedReach.Enabled;
            MemWriteFeature<FullBright>.Instance.Enabled = cfg.FullBright.Enabled;
            //MemPatchFeature<DisableShadows>.Instance.Enabled = cfg.DisableShadows;
            MemWriteFeature<DisableGrass>.Instance.Enabled = cfg.DisableGrass;
            MemWriteFeature<ClearWeather>.Instance.Enabled = cfg.ClearWeather;
            MemWriteFeature<NoVisor>.Instance.Enabled = cfg.NoVisor;
            MemWriteFeature<ThermalVision>.Instance.Enabled = cfg.ThermalVision;
            MemWriteFeature<NightVision>.Instance.Enabled = cfg.NightVision;
            MemWriteFeature<WideLean>.Instance.Enabled = cfg.WideLean.Enabled;
            MemWriteFeature<LongJump>.Instance.Enabled = cfg.LongJump.Enabled;
            MemWriteFeature<ThirdPerson>.Instance.Enabled = cfg.ThirdPerson;
            MemWriteFeature<DisableFrostbite>.Instance.Enabled = cfg.DisableFrostbite;
            MemWriteFeature<OwlMode>.Instance.Enabled = cfg.OwlMode;
            MemWriteFeature<DisableHeadBobbing>.Instance.Enabled = cfg.DisableHeadBobbing;
            //MemPatchFeature<StreamerMode>.Instance.Enabled = cfg.StreamerMode;
            MemWriteFeature<HideRaidCode>.Instance.Enabled = cfg.HideRaidCode;
            MemWriteFeature<InstantPlant>.Instance.Enabled = cfg.InstantPlant;
            MemWriteFeature<MedPanel>.Instance.Enabled = cfg.MedPanel;
            MemWriteFeature<DisableInventoryBlur>.Instance.Enabled = cfg.DisableInventoryBlur;
            //MemPatchFeature<DisableScreenEffects>.Instance.Enabled = cfg.DisableScreenEffects;
            MemWriteFeature<BigHead>.Instance.Enabled = cfg.BigHead.Enabled;
            //MemPatchFeature<SilentLoot>.Instance.Enabled = cfg.SilentLoot.Enabled;
            //MemPatchFeature<VisibilityLinecast>.Instance.Enabled = cfg.VisCheck.Enabled;
        }

        private void ToggleSettingsPanel(UIElement panel)
        {
            panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateSpecificAimbotOption(string optionName, bool isSelected)
        {
            if (_isLoadingAimbotOptions)
                return;

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            XMLogging.WriteLine($"Updated aimbot option: {optionName} = {isSelected}");
        }
        #endregion

        #region Events
        private void MemWritingCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tag)
            {
                var shouldProceed = true;
                var value = cb.IsChecked == true;

                switch (tag)
                {
                    case "MemWritesEnabled":
                        if (value && !MemWrites.Config.MemWritesEnabled)
                        {
                            shouldProceed = ConfirmMemoryWritingEnable(false);
                            if (!shouldProceed)
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    cb.IsChecked = false;
                                }), DispatcherPriority.Render);
                                return;
                            }
                        }
                        break;
                }

                XMLogging.WriteLine($"[Checkbox] {cb.Name} changed to {value}");

                switch (tag)
                {
                    case "MemWritesEnabled":
                        //Config.MemWrites.MemWritesEnabled = value;
                        MemWrites.Enabled = value;
                        ToggleMemWritingControls();
                        break;
                    case "RageMode":
                        MemWriteFeature<RageMode>.Instance.Enabled = value;
                        break;
                    case "EnableAimbot":
                        MemWriteFeature<Aimbot>.Instance.Enabled = value;
                        ToggleAimbotControls();
                        break;
                    case "AimbotSafeLock":
                        MemWrites.Config.Aimbot.SilentAim.SafeLock = value;
                        break;
                    case "AimbotAutoBone":
                        MemWrites.Config.Aimbot.SilentAim.AutoBone = value;
                        break;
                    case "AimbotDisableReLock":
                        MemWrites.Config.Aimbot.DisableReLock = value;
                        break;
                    case "HeadshotAI":
                        MemWrites.Config.Aimbot.HeadshotAI = value;
                        break;
                    case "AimbotRandomBone":
                        MemWrites.Config.Aimbot.RandomBone.Enabled = value;
                        ToggleAimbotRandomBoneControls();
                        break;
                    case "NoWeaponMalfunctions":
                        MemWriteFeature<NoWepMalfPatch>.Instance.Enabled = value;
                        break;
                    case "MagDrills":
                        MemWriteFeature<FastLoadUnload>.Instance.Enabled = value;
                        break;
                    case "FastWeaponOps":
                        MemWriteFeature<FastWeaponOps>.Instance.Enabled = value;
                        break;
                    case "DisableWeaponCollision":
                        MemWriteFeature<DisableWeaponCollision>.Instance.Enabled = value;
                        break;
                    //case "RemoveableAttachments":
                    //    MemPatchFeature<RemoveableAttachments>.Instance.Enabled = value;
                    //    break;
                    case "NoRecoil":
                        MemWriteFeature<NoRecoil>.Instance.Enabled = value;
                        ToggleNoRecoilControls();
                        break;
                    case "InfiniteStamina":
                        MemWriteFeature<InfStamina>.Instance.Enabled = value;
                        break;
                    case "MoveSpeed":
                        MemWriteFeature<MoveSpeed>.Instance.Enabled = value;
                        ToggleMoveSpeedControls();
                        break;
                    case "TimeOfDay":
                        MemWriteFeature<TimeOfDay>.Instance.Enabled = value;
                        ToggleTimeOfDayControls();
                        break;
                    //case "DisableShadows":
                    //    MemPatchFeature<DisableShadows>.Instance.Enabled = value;
                    //    break;
                    case "DisableGrass":
                        MemWriteFeature<DisableGrass>.Instance.Enabled = value;
                        break;
                    case "ClearWeather":
                        MemWriteFeature<ClearWeather>.Instance.Enabled = value;
                        break;
                    case "LTW":
                        MemWriteFeature<LootThroughWalls>.Instance.Enabled = value;
                        ToggleLTWControls();
                        break;
                    //case "SilentLoot":
                    //    MemPatchFeature<SilentLoot>.Instance.Enabled = value;
                    //    ToggleSilentLootControls();
                    //    break;
                    case "ExtendedReach":
                        MemWriteFeature<ExtendedReach>.Instance.Enabled = value;
                        ToggleExtendedReachControls();
                        break;
                    case "FullBright":
                        MemWriteFeature<FullBright>.Instance.Enabled = value;
                        ToggleFullBrightControls();
                        break;
                    case "NoVisor":
                        MemWriteFeature<NoVisor>.Instance.Enabled = value;
                        break;
                    case "ThermalVision":
                        MemWriteFeature<ThermalVision>.Instance.Enabled = value;
                        break;
                    case "NightVision":
                        MemWriteFeature<NightVision>.Instance.Enabled = value;
                        break;
                    case "WideLean":
                        MemWriteFeature<WideLean>.Instance.Enabled = value;
                        ToggleWideLeanControls();
                        break;
                    case "ThirdPerson":
                        MemWriteFeature<ThirdPerson>.Instance.Enabled = value;
                        break;
                    case "DisableFrostbite":
                        MemWriteFeature<DisableFrostbite>.Instance.Enabled = value;
                        break;
                    case "OwlMode":
                        MemWriteFeature<OwlMode>.Instance.Enabled = value;
                        break;
                    //case "FOVChanger":
                    //    MemPatchFeature<FOVChanger>.Instance.Enabled = value;
                    //    ToggleFOVControls();
                    //    break;
                    //case "StreamerMode":
                    //    MemPatchFeature<StreamerMode>.Instance.Enabled = value;
                    //    break;
                    case "HideRaidCode":
                        MemWriteFeature<HideRaidCode>.Instance.Enabled = value;
                        break;
                    case "InstantPlant":
                        MemWriteFeature<InstantPlant>.Instance.Enabled = value;
                        break;
                    case "DisableInventoryBlur":
                        MemWriteFeature<DisableInventoryBlur>.Instance.Enabled = value;
                        break;
                    case "MedPanel":
                        MemWriteFeature<MedPanel>.Instance.Enabled = value;
                        break;
                    //case "DisableScreenEffects":
                    //    MemPatchFeature<DisableScreenEffects>.Instance.Enabled = value;
                    //    break;
                    case "DisableHeadBobbing":
                        MemWriteFeature<DisableHeadBobbing>.Instance.Enabled = value;
                        break;
                    case "FastDuck":
                        MemWriteFeature<FastDuck>.Instance.Enabled = value;
                        break;
                    case "MuleMode":
                        MemWriteFeature<MuleMode>.Instance.Enabled = value;
                        break;
                    case "NoInertia":
                        MemWriteFeature<NoInertia>.Instance.Enabled = value;
                        break;
                    case "LongJump":
                        MemWriteFeature<LongJump>.Instance.Enabled = value;
                        ToggleLongJumpControls();
                        break;
                    case "BigHeads":
                        MemWriteFeature<BigHead>.Instance.Enabled = value;
                        ToggleBigHeadControls();
                        break;
                    //case "VisCheck":
                    //    MemPatchFeature<VisibilityLinecast>.Instance.Enabled = value;
                    //    ToggleVisCheckControls();
                    //    break;
                    //case "IgnoreAi":
                    //    MemWrites.Config.VisCheck.IgnoreAi = value;
                    //    break;
                }

                Config.Save();
                XMLogging.WriteLine("Saved Convig");
            }
        }

        private void MemWritingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is TextValueSlider slider && slider.Tag is string tag)
            {
                var intValue = (int)e.NewValue;
                var floatValue = (float)e.NewValue;
                var roundedValue = (float)Math.Round(floatValue, 1);

                switch (tag)
                {
                    case "AimbotFOV":
                        MemWrites.Config.Aimbot.FOV = floatValue;
                        break;
                    case "AimbotDistance":
                        MemWrites.Config.Aimbot.Distance = floatValue;
                        break;
                    case "NoRecoilAmount":
                        MemWrites.Config.NoRecoilAmount = intValue;
                        break;
                    case "NoSwayAmount":
                        MemWrites.Config.NoSwayAmount = intValue;
                        break;

                    case "RNGHead":
                    case "RNGTorso":
                    case "RNGArms":
                    case "RNGLegs":
                        {
                            var rng = MemWrites.Config.Aimbot.RandomBone;

                            switch (tag)
                            {
                                case "RNGHead": rng.HeadPercent = intValue; break;
                                case "RNGTorso": rng.TorsoPercent = intValue; break;
                                case "RNGArms": rng.ArmsPercent = intValue; break;
                                case "RNGLegs": rng.LegsPercent = intValue; break;
                            }

                            var total = rng.HeadPercent + rng.TorsoPercent + rng.ArmsPercent + rng.LegsPercent;

                            if (total > 100)
                            {
                                var overflow = total - 100;
                                var sliders = new Dictionary<string, Action<int>>
                                {
                                    ["RNGHead"] = v => rng.HeadPercent = v,
                                    ["RNGTorso"] = v => rng.TorsoPercent = v,
                                    ["RNGArms"] = v => rng.ArmsPercent = v,
                                    ["RNGLegs"] = v => rng.LegsPercent = v
                                };

                                var values = new Dictionary<string, int>
                                {
                                    ["RNGHead"] = rng.HeadPercent,
                                    ["RNGTorso"] = rng.TorsoPercent,
                                    ["RNGArms"] = rng.ArmsPercent,
                                    ["RNGLegs"] = rng.LegsPercent
                                };

                                foreach (var key in values.Keys.ToList())
                                {
                                    if (key == tag) continue;

                                    if (overflow == 0) break;

                                    int reduceBy = Math.Min(values[key], overflow);
                                    values[key] -= reduceBy;
                                    overflow -= reduceBy;
                                }

                                foreach (var kv in values)
                                    sliders[kv.Key](kv.Value);

                                sldrAimbotRNGHead.Value = rng.HeadPercent;
                                sldrAimbotRNGTorso.Value = rng.TorsoPercent;
                                sldrAimbotRNGArms.Value = rng.ArmsPercent;
                                sldrAimbotRNGLegs.Value = rng.LegsPercent;
                            }

                            break;
                        }
                    case "LTWZoom":
                        MemWrites.Config.LootThroughWalls.ZoomAmount = roundedValue;
                        break;
                    //case "SilentLootDistance":
                    //    MemWrites.Config.SilentLoot.Distance = roundedValue;
                    //    break;
                    //case "SilentLootMaxDistance":
                    //    MemWrites.Config.SilentLoot.MaxDistance = roundedValue;
                    //    break;
                    case "ExtendedReachDistance":
                        MemWrites.Config.ExtendedReach.Distance = roundedValue;
                        break;
                    case "TimeOfDayHour":
                        MemWrites.Config.TimeOfDay.Hour = intValue;
                        break;
                    case "FullBrightIntensity":
                        MemWrites.Config.FullBright.Intensity = floatValue;
                        break;
                    case "LeanAmt":
                        MemWrites.Config.WideLean.Amount = roundedValue;
                        break;
                    case "JumpMultiplier":
                        MemWrites.Config.LongJump.Multiplier = roundedValue;
                        break;
                    case "MoveSpeedMultiplier":
                        MemWrites.Config.MoveSpeed.Multiplier = floatValue;
                        break;
                    case "BigHeadScale":
                        MemWrites.Config.BigHead.Scale = floatValue;
                        break;
                    //case "VisLowDist":
                    //    MemWrites.Config.VisCheck.LowDist = floatValue;
                    //    break;
                    //case "VisMidDist":
                    //    MemWrites.Config.VisCheck.MidDist = floatValue;
                    //    break;
                    //case "VisfarDist":
                    //    MemWrites.Config.VisCheck.FarDist = floatValue;
                    //    break;
                    //case "FOVBase":
                    //    MemWrites.Config.FOV.Base = intValue;
                    //    break;
                    //case "ADSFOV":
                    //    MemWrites.Config.FOV.ADS = intValue;
                    //    break;
                    //case "TPPFOV":
                    //    MemWrites.Config.FOV.ThirdPerson = intValue;
                    //    break;
                    //case "ZoomFOV":
                    //    MemWrites.Config.FOV.InstantZoom = intValue;
                    //    break;
                }

                Config.Save();
            }
        }

        private void MemWritingRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string mode)
            {
                MemWrites.Config.Aimbot.TargetingMode = mode switch
                {
                    "FOV" => AimbotTargetingMode.FOV,
                    "CQB" => AimbotTargetingMode.CQB,
                    _ => MemWrites.Config.Aimbot.TargetingMode
                };

                Config.Save();
            }
        }

        private void MemWritingButton_Clicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "NoRecoilPanel":
                        ToggleSettingsPanel(pnlNoRecoil);
                        break;
                    case "MoveSpeedPanel":
                        ToggleSettingsPanel(pnlMoveSpeed);
                        break;
                    case "WideLeanPanel":
                        ToggleSettingsPanel(pnlWideLean);
                        break;
                    case "LongJumpPanel":
                        ToggleSettingsPanel(pnlLongJump);
                        break;
                    case "TimeOfDayPanel":
                        ToggleSettingsPanel(pnlTimeOfDay);
                        break;
                    case "FullBrightPanel":
                        ToggleSettingsPanel(pnlFullBright);
                        break;
                    case "LTWPanel":
                        ToggleSettingsPanel(pnlLTW);
                        break;
                    case "ExtendedReachPanel":
                        ToggleSettingsPanel(pnlExtendedReach);
                        break;
                    case "FOVPanel":
                        ToggleSettingsPanel(pnlFOV);
                        break;
                    case "BigHeadsPanel":
                        ToggleSettingsPanel(pnlBigHeads);
                        break;

                }

                Config.Save();
                XMLogging.WriteLine("Saved Convig");
            }
        }

        private void cboTargetBone_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Config?.MemWrites?.Aimbot == null)
                return;

            if (cboTargetBone.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content is string boneName)
            {
                MemWrites.Config.Aimbot.Bone = boneName switch
                {
                    "Head" => Bones.HumanHead,
                    "Neck" => Bones.HumanNeck,
                    "Thorax" => Bones.HumanSpine3,
                    "Stomach" => Bones.HumanPelvis,
                    "Legs" => Bones.Legs,
                    _ => Bones.HumanHead
                };
                Config.Save();
            }
        }

        private void aimbotOptionsCheckComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingAimbotOptions)
                return;

            foreach (CheckComboBoxItem item in ccbAimbotOptions.Items)
            {
                var option = item.Content.ToString();
                var isSelected = item.IsSelected;

                switch (option)
                {
                    case "Safe Lock":
                        MemWrites.Config.Aimbot.SilentAim.SafeLock = isSelected;
                        break;
                    case "Disable Re-Lock":
                        MemWrites.Config.Aimbot.DisableReLock = isSelected;
                        break;
                    case "Auto Bone":
                        MemWrites.Config.Aimbot.SilentAim.AutoBone = isSelected;
                        break;
                    case "Headshot AI":
                        MemWrites.Config.Aimbot.HeadshotAI = isSelected;
                        break;
                    case "Random Bone":
                        MemWrites.Config.Aimbot.RandomBone.Enabled = isSelected;
                        ToggleAimbotRandomBoneControls();
                        break;
                }
            }

            Config.Save();
            XMLogging.WriteLine("Saved aimbot options settings");
        }

        private struct NativeHookTestState
        {
            public uint HasRun;
            public uint IsRunning;
            public ulong UserData;
            public ulong ReturnAddr;
        }

        private async void btnAntiAFK_Click(object sender, RoutedEventArgs e)
        {
            btnAntiAFK.Content = "Please Wait...";
            btnAntiAFK.IsEnabled = false;

            try
            {
                await Task.Run(() => // Run on non ui thread
                {
                    MemWriteFeature<AntiAfk>.Instance.Set();
                });

                NotificationsShared.Success("Anti-AFK is Set!\n\n NOTE: If you leave the Main Menu, you may need to re-set this.");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"ERROR Setting Anti-AFK! Your memory may be paged out, try close and re-open the game and try again.\n\n ${ex}");
            }
            finally
            {
                btnAntiAFK.Content = "Anti-AFK";
                btnAntiAFK.IsEnabled = true;
            }
        }
private static class ObservedPlayerViewOffsets
{
    public const uint RaidId        = 0x20;   // int
    public const uint Voice         = 0x40;   // string
    public const uint IsVisible     = 0x64;   // bool
    public const uint WorldTime     = 0x68;   // float
    public const uint Id            = 0x7C;   // int
    public const uint GroupId       = 0x80;   // string
    public const uint TeamId        = 0x88;   // string
    public const uint UsedSkeleton  = 0x90;   // bool
    public const uint Side          = 0x94;   // int (enum)
    public const uint IsAI          = 0xA0;   // bool
    public const uint ProfileId     = 0xA8;   // string
    public const uint VoipId        = 0xB0;   // string
    public const uint NickName      = 0xB8;   // string
    public const uint AccountId     = 0xC0;   // string
}

private async void btnTest_Click(object sender, RoutedEventArgs e)
{
            btnTest.Content = "Please Wait...";
            btnTest.IsEnabled = false;

            try
            {
                await Task.Run(() => // Run on non ui thread
                {
                    //MemWriteFeature<HideRaidCode>.Instance.Set();
                });

                NotificationsShared.Success("HideRaidCode is Set!\n\n NOTE: If you leave the Main Menu, you may need to re-set this.");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"ERROR Setting HideRaidCode! Your memory may be paged out, try close and re-open the game and try again.\n\n ${ex}");
            }
            finally
            {
                btnTest.Content = "Test HideRaidCode";
                btnTest.IsEnabled = true;
            }
}






        #endregion

        #endregion

        #region Config Import Handling
        public static class MemoryWritingImportHandler
        {
            public enum MemoryWritingDecision
            {
                DisableAll,
                EnableBasicOnly,
                EnableAll,
                KeepCurrent
            }

            /// <summary>
            /// Shows memory writing confirmation dialogs from the Memory Writing panel
            /// </summary>
            public static MemoryWritingDecision ShowMemoryWritingConfirmation(bool hasBasicMemWrites, bool hasAdvancedMemWrites)
            {
                return Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!hasBasicMemWrites)
                        return MemoryWritingDecision.KeepCurrent;

                    if (hasAdvancedMemWrites)
                    {
                        var advancedResult = MessageBox.Show(
                            "⚠️ ADVANCED MEMORY WRITING DETECTED ⚠️\n\n" +
                            "The configuration you're importing has Advanced Memory Writing features enabled.\n\n" +
                            "Advanced features include things such as:\n" +
                            "• Shellcode injection\n" +
                            "• Advanced chams (vischeck)\n" +
                            "• FOV changer\n" +
                            "• Disable Screen Effects (eg flash bangs etc)\n" +
                            "• Streamer Mode\n\n" +
                            "⚠️ WARNING: These features may carry a higher detection risk!\n\n" +
                            "Do you want to enable Advanced Memory Writing features?",
                            "Advanced Memory Writing Configuration",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (advancedResult == MessageBoxResult.Yes)
                        {
                            return MemoryWritingDecision.EnableAll;
                        }
                        else if (hasBasicMemWrites)
                        {
                            var basicResult = MessageBox.Show(
                                "Advanced Memory Writing has been disabled.\n\n" +
                                "However, this configuration also contains Basic Memory Writing features:\n\n" +
                                "• Aimbot, No Recoil, Infinite Stamina\n" +
                                "• Movement modifications (Speed, No Inertia, etc.)\n" +
                                "• Visual modifications (Night Vision, etc.)\n" +
                                "• And other game modifications\n\n" +
                                "⚠️ WARNING: These features still carry detection risk!\n\n" +
                                "Do you want to enable Basic Memory Writing features?",
                                "Basic Memory Writing Configuration",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);

                            return basicResult == MessageBoxResult.Yes ? MemoryWritingDecision.EnableBasicOnly : MemoryWritingDecision.DisableAll;
                        }
                        else
                        {
                            return MemoryWritingDecision.DisableAll;
                        }
                    }
                    else if (hasBasicMemWrites)
                    {
                        var basicResult = MessageBox.Show(
                            "⚠️ MEMORY WRITING DETECTED ⚠️\n\n" +
                            "The configuration you're importing has Memory Writing features enabled.\n\n" +
                            "Memory writing features include:\n" +
                            "• Aimbot, No Recoil, Infinite Stamina\n" +
                            "• Movement modifications (Speed, No Inertia, etc.)\n" +
                            "• Visual modifications (Night Vision, etc.)\n" +
                            "• And other game modifications\n\n" +
                            "⚠️ WARNING: These features carry increased detection risk!\n\n" +
                            "Do you want to enable Memory Writing features?",
                            "Memory Writing Configuration",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        return basicResult == MessageBoxResult.Yes ? MemoryWritingDecision.EnableBasicOnly : MemoryWritingDecision.DisableAll;
                    }

                    return MemoryWritingDecision.KeepCurrent;
                });
            }

            /// <summary>
            /// Applies the memory writing decision to the imported config
            /// </summary>
            public static void ApplyMemoryWritingDecision(Config importedConfig, MemoryWritingDecision decision)
            {
                switch (decision)
                {
                    case MemoryWritingDecision.DisableAll:
                        importedConfig.MemWrites.MemWritesEnabled = false;
                        //importedConfig.MemWrites.AdvancedMemWrites = false;
                        XMLogging.WriteLine("[Config] User chose to disable all Memory Writing features during import");
                        NotificationsShared.Info("[Config] All Memory Writing features have been disabled. You can enable them later in the Memory Writing panel if needed.");
                        break;

                    case MemoryWritingDecision.EnableBasicOnly:
                        importedConfig.MemWrites.MemWritesEnabled = true;
                        //importedConfig.MemWrites.AdvancedMemWrites = false;
                        XMLogging.WriteLine("[Config] User chose to enable Basic Memory Writing features only during import");
                        NotificationsShared.Warning("[Config] Basic Memory Writing features are enabled. Advanced features have been disabled. Please be aware of the associated risks.");
                        break;

                    case MemoryWritingDecision.EnableAll:
                        importedConfig.MemWrites.MemWritesEnabled = true;
                        //importedConfig.MemWrites.AdvancedMemWrites = true;
                        XMLogging.WriteLine("[Config] User chose to keep all Memory Writing features enabled during import");
                        NotificationsShared.Warning("[Config] All Memory Writing features including Advanced features are enabled. Please be aware of the significant risks associated with these features.");
                        break;

                    case MemoryWritingDecision.KeepCurrent:
                        break;
                }
            }
        }

        /// <summary>
        /// Called from GeneralSettingsControl when importing config with memory writing features
        /// </summary>
        public static MemoryWritingImportHandler.MemoryWritingDecision HandleConfigImportMemoryWriting(Config importedConfig)
        {
            var hasBasicMemWrites = importedConfig.MemWrites.MemWritesEnabled;
            //var hasAdvancedMemWrites = importedConfig.MemWrites.AdvancedMemWrites;

            return MemoryWritingImportHandler.ShowMemoryWritingConfirmation(hasBasicMemWrites, false);
        }

        /// <summary>
        /// Shows confirmation when user manually enables memory writing features
        /// </summary>
        public bool ConfirmMemoryWritingEnable(bool isAdvanced = false)
        {
            return Dispatcher.Invoke(() =>
            {
                if (isAdvanced)
                {
                    var result = MessageBox.Show(
                        "⚠️ ENABLING ADVANCED MEMORY WRITING ⚠️\n\n" +
                        "You are about to enable Advanced Memory Writing features.\n\n" +
                        "Advanced features include things such as:\n" +
                        "• Shellcode injection\n" +
                        "• Advanced chams (vischeck)\n" +
                        "• FOV changer\n" +
                        "• Disable Screen Effects (eg flash bangs etc)\n" +
                        "• Streamer Mode\n\n" +
                        "⚠️ WARNING: These features may carry a higher detection risk!\n\n" +
                        "Are you sure you want to enable Advanced Memory Writing?",
                        "Advanced Memory Writing Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    return result == MessageBoxResult.Yes;
                }
                else
                {
                    var result = MessageBox.Show(
                        "⚠️ ENABLING MEMORY WRITING ⚠️\n\n" +
                        "You are about to enable Memory Writing features.\n\n" +
                        "Memory writing features include:\n" +
                        "• Aimbot, No Recoil, Infinite Stamina\n" +
                        "• Movement modifications (Speed, No Inertia, etc.)\n" +
                        "• Visual modifications (Night Vision, etc.)\n" +
                        "• And other game modifications\n\n" +
                        "⚠️ WARNING: These features carry increased detection risk!\n\n" +
                        "Are you sure you want to enable Memory Writing?",
                        "Memory Writing Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    return result == MessageBoxResult.Yes;
                }
            });
        }
        #endregion
    }
}