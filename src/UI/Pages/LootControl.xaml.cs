using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Patches;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.LootFilters;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using HandyControl.Controls;
using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CheckBox = System.Windows.Controls.CheckBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages
{
    /// <summary>
    /// Interaction logic for LootSettingsControl.xaml
    /// </summary>
    public partial class LootSettingsControl : UserControl
    {
        #region Fields and Properties
        private const int INTERVAL = 100; // 0.1 second
        private Point _dragStartPoint;
        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        private static Config Config => Program.Config;

        /// <summary>
        /// Tracked Containers Dictionary.
        /// TRUE if the container should be displayed.
        /// </summary>
        internal static ConcurrentDictionary<string, bool> TrackedContainers { get; private set; } = new();

        private bool _isLoadingLootFilterSettings = false;
        private readonly string[] _availableLootFilterOptions = new string[]
        {
            "Show Meds",
            "Show Food",
            "Show Weapons",
            "Show Backpacks",
            "Corpse Markers (X)"
        };
        #endregion

        public LootSettingsControl()
        {
            InitializeComponent();
            TooltipManager.AssignLootTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null || !EftDataManager.IsInitialized)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("LootSettings");
                ExpanderManager.Instance.RegisterExpanders(this, "LootSettings",
                    expGeneralSettings,
                    expPriceSettings,
                    expQuickFilters,
                    expContainerOptions);

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

        #region Loot Settings Panel
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
                LoadLootSettings();
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

        #region Loot Settings
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            // General Settings
            chkProcessLoot.Checked += LootSettingsCheckbox_Checked;
            chkProcessLoot.Unchecked += LootSettingsCheckbox_Checked;
            chkShowLootWishlist.Checked += LootSettingsCheckbox_Checked;
            chkShowLootWishlist.Unchecked += LootSettingsCheckbox_Checked;

            // Price Settings`
            sldrPriceRange.ValueChanged += sldrPriceRange_ValueChanged;
            sldrMinCorpseValue.ValueChanged += sldrMinCorpseValue_ValueChanged;
            chkPricePerSlot.Checked += LootSettingsCheckbox_Checked;
            chkPricePerSlot.Unchecked += LootSettingsCheckbox_Checked;
            rdbFleaPrices.Checked += LootSettingsRadioButton_Checked;
            rdbTraderPrices.Checked += LootSettingsRadioButton_Checked;

            // Quick Filters
            ccbLootFilters.SelectionChanged += ccbLootFilters_SelectionChanged;
            txtLootToSearch.TextChanged += txtLootToSearch_TextChanged;

            // Container Options
            chkStaticContainers.Checked += LootSettingsCheckbox_Checked;
            chkStaticContainers.Unchecked += LootSettingsCheckbox_Checked;
            chkContainersSelectAll.Checked += LootSettingsCheckbox_Checked;
            chkContainersSelectAll.Unchecked += LootSettingsCheckbox_Checked;
            chkContainersHideSearched.Checked += LootSettingsCheckbox_Checked;
            chkContainersHideSearched.Unchecked += LootSettingsCheckbox_Checked;
        }

        private void LoadLootSettings()
        {
            // General Settings
            chkProcessLoot.IsChecked = Config.ProcessLoot;
            chkShowLootWishlist.IsChecked = Config.LootWishlist;

            // Price Settings
            sldrPriceRange.ValueStart = Config.MinLootValue;
            sldrPriceRange.ValueEnd = Config.MinValuableLootValue;
            sldrMinCorpseValue.Value = Config.MinCorpseValue;
            chkPricePerSlot.IsChecked = Config.LootPPS;
            var rdb = (Config.LootPriceMode == LootPriceMode.FleaMarket) ? rdbFleaPrices : rdbTraderPrices;
            rdb.IsChecked = true;

            // Quick Filters
            InitializeLootFilterOptions();

            // Containers
            chkStaticContainers.IsChecked = Config.Containers.Show;
            chkContainersHideSearched.IsChecked = Config.Containers.HideSearched;

            ToggleLootControls();
        }

        private void UpdateLootFilterSelections()
        {
            if (_isLoadingLootFilterSettings)
                return;

            var optionsToUpdate = new Dictionary<string, bool>
            {
                ["Show Meds"] = LootFilterControl.ShowMeds,
                ["Show Food"] = LootFilterControl.ShowFood,
                ["Show Backpacks"] = LootFilterControl.ShowBackpacks,
                ["Show Weapons"] = LootFilterControl.ShowWeapons,
                ["Corpse Markers (X)"] = Config.ShowCorpseMarkers
            };

            foreach (CheckComboBoxItem item in ccbLootFilters.Items)
            {
                var content = item.Content.ToString();

                if (optionsToUpdate.TryGetValue(content, out bool shouldBeSelected))
                    if (item.IsSelected != shouldBeSelected)
                        item.IsSelected = shouldBeSelected;
            }
        }

        public void UpdateSpecificLootFilterOption(string optionName, bool isSelected)
        {
            if (_isLoadingLootFilterSettings)
                return;

            foreach (CheckComboBoxItem item in ccbLootFilters.Items)
            {
                if (item.Content.ToString() == optionName)
                {
                    item.IsSelected = isSelected;
                    break;
                }
            }

            Config.Save();
            XMLogging.WriteLine($"Updated loot filter option: {optionName} = {isSelected}");
        }

        private void InitializeLootFilterOptions()
        {
            _isLoadingLootFilterSettings = true;

            try
            {
                ccbLootFilters.Items.Clear();

                foreach (var option in _availableLootFilterOptions)
                {
                    ccbLootFilters.Items.Add(new CheckComboBoxItem { Content = option });
                }
            }
            finally
            {
                _isLoadingLootFilterSettings = false;
            }

            UpdateLootFilterSelections();
        }

        private void ToggleLootControls()
        {
            var enabled = Config.ProcessLoot;

            // General Settings
            chkShowLootWishlist.IsEnabled = enabled;

            // Price Settings
            sldrPriceRange.IsEnabled = enabled;
            sldrMinCorpseValue.IsEnabled = enabled;
            chkPricePerSlot.IsEnabled = enabled;
            rdbFleaPrices.IsEnabled = enabled;
            rdbTraderPrices.IsEnabled = enabled;

            // Quick Filters
            ccbLootFilters.IsEnabled = enabled;
            txtLootToSearch.IsEnabled = enabled;

            // Container Options
            ToggleContainerControls();
            RefreshContainerData();
        }

        private void ToggleContainerControls()
        {
            var enabled = Config.Containers.Show;

            listContainers.IsEnabled = enabled;
            chkContainersSelectAll.IsEnabled = enabled;
            chkContainersHideSearched.IsEnabled = enabled;
        }

        /// <summary>
        /// Checks if a container is being tracked by its Item ID.
        /// </summary>
        /// <param name="id">Container Item ID</param>
        /// <returns>True if being tracked, otherwise False.</returns>
        internal static bool ContainerIsTracked(string id)
        {
            return TrackedContainers.TryGetValue(id, out bool tracked) && tracked;
        }

        /// <summary>
        /// Selects/deselects all containers.
        /// </summary>
        private void ModifyAllContainers(bool selectAll)
        {
            foreach (var key in TrackedContainers.Keys.ToList())
            {
                TrackedContainers[key] = selectAll;
            }

            if (listContainers.ItemsSource != null)
            {
                foreach (ContainerListItem item in listContainers.ItemsSource)
                {
                    item.IsSelected = selectAll;
                }
            }

            Config.Containers.Selected = selectAll
                ? TrackedContainers.Keys.ToList()
                : new List<string>();

            listContainers.Items.Refresh();
        }

        private void RefreshContainerData()
        {
            TrackedContainers = new(EftDataManager.AllContainers.ToDictionary(x => x.Key, x => false));

            foreach (var id in Config.Containers.Selected)
            {
                if (TrackedContainers.ContainsKey(id))
                    TrackedContainers[id] = true;
            }

            var entries = EftDataManager.AllContainers.Values
                                        .OrderBy(x => x.Name)
                                        .Select(x => new ContainerListItem(x)
                                        {
                                            IsSelected = TrackedContainers.TryGetValue(x.BsgId, out bool isSelected) && isSelected
                                        })
                                        .ToArray();

            listContainers.ItemsSource = entries;
        }

        private bool IsFilterOptionSelected(string option)
        {
            foreach (CheckComboBoxItem item in ccbLootFilters.SelectedItems)
            {
                if (item.Content.ToString() == option)
                    return true;
            }

            return false;
        }
        #endregion

        #region Events
        private void LootSettingsCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string tag)
            {
                var value = cb.IsChecked == true;

                switch (tag)
                {
                    case "ProcessLoot":
                        Config.ProcessLoot = value;
                        ToggleLootControls();
                        break;
                    case "ShowWishlist":
                        Config.LootWishlist = value;
                        break;
                    case "PricePerSlot":
                        Config.LootPPS = value;
                        break;
                    case "ShowMeds":
                        LootFilterControl.ShowMeds = value;
                        break;
                    case "ShowFood":
                        LootFilterControl.ShowFood = value;
                        break;
                    case "ShowWeapons":
                        LootFilterControl.ShowWeapons = value;
                        break;
                    case "ShowBackpacks":
                        LootFilterControl.ShowBackpacks = value;
                        break;
                    case "CorpseMarkers(X)":
                        Config.ShowCorpseMarkers = value;
                        break;
                    case "StaticContainers":
                        Config.Containers.Show = value;
                        ToggleContainerControls();
                        break;
                    case "SelectAllContainers":
                        ModifyAllContainers(value);
                        break;
                    case "HideSearchedContainers":
                        Config.Containers.HideSearched = value;
                        break;
                }

                Config.Save();
                XMLogging.WriteLine("Saved Convig");
            }
        }

        private void LootSettingsRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string mode)
            {
                Config.LootPriceMode = mode switch
                {
                    "FleaMarket" => LootPriceMode.FleaMarket,
                    "Trader" => LootPriceMode.Trader,
                    _ => Config.LootPriceMode
                };

                Config.Save();
            }
        }

        /// <summary>
        /// Handles individual container checkbox state changes.
        /// </summary>
        private void ContainerCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ContainerListItem item)
            {
                var isChecked = checkBox.IsChecked == true;
                item.IsSelected = isChecked;

                TrackedContainers[item.Id] = isChecked;

                if (!isChecked && chkContainersSelectAll.IsChecked == true)
                    chkContainersSelectAll.IsChecked = false;

                Config.Containers.Selected = TrackedContainers
                    .Where(x => x.Value is true)
                    .Select(x => x.Key)
                    .ToList();
            }
        }

        private void txtLootToSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            LootFilterControl.SearchString = txtLootToSearch.Text;
        }

        private void sldrPriceRange_ValueChanged(object sender, RoutedPropertyChangedEventArgs<HandyControl.Data.DoubleRange> e)
        {
            var sldr = sldrPriceRange;

            Config.MinLootValue = (int)sldr.ValueStart;
            Config.MinValuableLootValue = (int)sldr.ValueEnd;
            Config.Save();
        }

        private void sldrMinCorpseValue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Config.MinCorpseValue = (int)e.NewValue;
            Config.Save();
        }

        private void ccbLootFilters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingLootFilterSettings)
                return;

            LootFilterControl.ShowMeds = IsFilterOptionSelected("Show Meds");
            LootFilterControl.ShowFood = IsFilterOptionSelected("Show Food");
            LootFilterControl.ShowWeapons = IsFilterOptionSelected("Show Weapons");
            LootFilterControl.ShowBackpacks = IsFilterOptionSelected("Show Backpacks");
            Config.ShowCorpseMarkers = IsFilterOptionSelected("Corpse Markers (X)");

            Config.Save();
            XMLogging.WriteLine("Saved loot filter settings");
        }
        #endregion
        #endregion

        #region Wrapper Classes
        /// <summary>
        /// Wrapper class for displaying container info in the UI.
        /// </summary>
        public sealed class ContainerListItem : INotifyPropertyChanged
        {
            public string Name { get; }
            public string Id { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public ContainerListItem(TarkovMarketItem container)
            {
                Name = container.ShortName;
                Id = container.BsgId;
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string prop) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
        #endregion
    }
}