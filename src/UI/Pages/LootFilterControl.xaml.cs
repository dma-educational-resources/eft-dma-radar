using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.LootFilters;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.UI.Controls;
using HandyControl.Controls;
using HandyControl.Tools;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MessageBox = eft_dma_radar.UI.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;
using Window = HandyControl.Controls.Window;

namespace eft_dma_radar.UI.Pages
{
    public partial class LootFilterControl : UserControl, INotifyPropertyChanged
    {
        #region Fields and Properties
        public event PropertyChangedEventHandler PropertyChanged;

        public static string SearchString;
        public static bool ShowMeds;
        public static bool ShowFood;
        public static bool ShowBackpacks;
        public static bool ShowWeapons;
        public static bool firstRemove = false;
        private static Config Config => Program.Config;
        private static bool ShowQuestItems => Config.QuestHelper.Enabled;

        private ObservableCollection<LootFilterGroup> _groupList = new();
        private LootFilterGroup _selectedGroup;
        private ObservableCollection<GroupedLootFilterEntry> _groupItems = new();
        private ICollectionView _groupItemsView;

        private Point _dragStartPoint;

        private bool _isDragging = false;
        private LootFilterGroup _draggedGroup = null;
        private ListViewItem _draggedItem = null;
        private int _insertIndex = -1;

        private bool _isItemDragging = false;
        private GroupedLootFilterEntry _draggedLootItem = null;
        private ListViewItem _draggedLootItemContainer = null;
        private int _itemInsertIndex = -1;

        private const int INTERVAL = 100; // 0.1 second
        private const string DEFAULT_GROUP_NAME = "default";

        private bool _isUpdatingControls = false;

        public ObservableCollection<LootFilterGroup> GroupList => _groupList;

        public event EventHandler CloseRequested;
        public event EventHandler BringToFrontRequested;
        public event EventHandler<PanelDragEventArgs> DragRequested;
        public event EventHandler<PanelResizeEventArgs> ResizeRequested;

        public LootFilterGroup SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (_selectedGroup != value)
                {
                    SetSelectedGroup(value);
                    OnPropertyChanged();
                }
            }
        }

        private HandyControl.Controls.PopupWindow _openColorPicker;
        #endregion

        public LootFilterControl()
        {
            InitializeComponent();
            TooltipManager.AssignLootFilterTooltips(this);

            this.Loaded += async (s, e) =>
            {
                while (MainWindow.Config == null)
                {
                    await Task.Delay(INTERVAL);
                }

                PanelCoordinator.Instance.SetPanelReady("LootFilter");
                ExpanderManager.Instance.RegisterExpanders(this, "LootFilter",
                    expLootFilterSettings,
                    expLootFilterItems);

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

        #region Filter Settings Panel
        #region Functions/Methods
        private void InitializeControlEvents()
        {
            Dispatcher.InvokeAsync(() =>
            {
                RegisterPanelEvents();
                RegisterSettingsEvents();
                RegisterDragDropEvents();
            });
        }

        private void RegisterPanelEvents()
        {
            // Header close button
            btnCloseHeader.Click += btnCloseHeader_Click;

            // Drag handling
            DragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
        }

        private void RegisterDragDropEvents()
        {
            // Filter Groups Drag & Drop
            lstFilterGroups.PreviewMouseLeftButtonDown += lstFilterGroups_PreviewMouseLeftButtonDown;
            lstFilterGroups.PreviewMouseMove += lstFilterGroups_PreviewMouseMove;
            lstFilterGroups.PreviewMouseLeftButtonUp += lstFilterGroups_PreviewMouseLeftButtonUp;
            lstFilterGroups.DragOver += lstFilterGroups_DragOver;
            lstFilterGroups.Drop += lstFilterGroups_Drop;

            // Loot Items Drag & Drop
            GroupedItemsListView.PreviewMouseLeftButtonDown += GroupedItemsListView_PreviewMouseLeftButtonDown;
            GroupedItemsListView.PreviewMouseMove += GroupedItemsListView_PreviewMouseMove;
            GroupedItemsListView.PreviewMouseLeftButtonUp += GroupedItemsListView_PreviewMouseLeftButtonUp;
            GroupedItemsListView.DragOver += GroupedItemsListView_DragOver;
            GroupedItemsListView.Drop += GroupedItemsListView_Drop;
        }
        #endregion

        #region Filter Groups Drag and Drop Implementation
        private void lstFilterGroups_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(lstFilterGroups);

            var hitTest = VisualTreeHelper.HitTest(lstFilterGroups, _dragStartPoint);
            if (hitTest?.VisualHit != null)
            {
                var listViewItem = FindParent<ListViewItem>(hitTest.VisualHit);
                if (listViewItem?.DataContext is LootFilterGroup group)
                {
                    var hitElement = hitTest.VisualHit as FrameworkElement;
                    var dragHandle = hitElement;

                    while (dragHandle != null)
                    {
                        if ((dragHandle is Border border && border.Cursor == Cursors.SizeAll) ||
                            (dragHandle is System.Windows.Shapes.Path shapePath && FindParent<Border>(shapePath)?.Cursor == Cursors.SizeAll))
                        {
                            _draggedGroup = group;
                            _draggedItem = listViewItem;
                            break;
                        }
                        dragHandle = VisualTreeHelper.GetParent(dragHandle) as FrameworkElement;
                    }
                }
            }
        }

        private void lstFilterGroups_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                _draggedGroup != null &&
                !_isDragging)
            {
                var currentPosition = e.GetPosition(lstFilterGroups);
                var diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;
                    var dragData = new DataObject("LootFilterGroup", _draggedGroup);

                    if (_draggedItem != null)
                        _draggedItem.Opacity = 0.5;

                    var result = DragDrop.DoDragDrop(lstFilterGroups, dragData, DragDropEffects.Move);

                    if (_draggedItem != null)
                        _draggedItem.Opacity = 1.0;

                    _isDragging = false;
                    _draggedGroup = null;
                    _draggedItem = null;
                }
            }
        }

        private void lstFilterGroups_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedGroup = null;
            _draggedItem = null;
        }

        private void lstFilterGroups_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            if (e.Data.GetDataPresent("LootFilterGroup"))
            {
                e.Effects = DragDropEffects.Move;

                var position = e.GetPosition(lstFilterGroups);
                _insertIndex = GetInsertIndex(lstFilterGroups, position);

                UpdateGroupDropIndicators(_insertIndex);
            }

            e.Handled = true;
        }

        private void lstFilterGroups_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("LootFilterGroup") &&
                e.Data.GetData("LootFilterGroup") is LootFilterGroup draggedGroup &&
                _insertIndex >= 0)
            {
                PerformGroupReorder(draggedGroup, _insertIndex);
            }

            ClearGroupDropIndicators();
            _insertIndex = -1;

            e.Handled = true;
        }

        private void UpdateGroupDropIndicators(int insertIndex)
        {
            ClearGroupDropIndicators();

            if (insertIndex >= 0 && insertIndex < lstFilterGroups.Items.Count)
            {
                var container = lstFilterGroups.ItemContainerGenerator.ContainerFromIndex(insertIndex) as ListViewItem;
                if (container != null)
                {
                    var indicator = FindChild<Border>(container, "TopDropIndicator");
                    if (indicator != null)
                        indicator.Visibility = Visibility.Visible;
                }
            }
            else if (insertIndex == lstFilterGroups.Items.Count && lstFilterGroups.Items.Count > 0)
            {
                var container = lstFilterGroups.ItemContainerGenerator.ContainerFromIndex(lstFilterGroups.Items.Count - 1) as ListViewItem;
                if (container != null)
                {
                    var indicator = FindChild<Border>(container, "BottomDropIndicator");
                    if (indicator != null)
                        indicator.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClearGroupDropIndicators()
        {
            for (int i = 0; i < lstFilterGroups.Items.Count; i++)
            {
                var container = lstFilterGroups.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    var topIndicator = FindChild<Border>(container, "TopDropIndicator");
                    var bottomIndicator = FindChild<Border>(container, "BottomDropIndicator");

                    if (topIndicator != null) topIndicator.Visibility = Visibility.Collapsed;
                    if (bottomIndicator != null) bottomIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void PerformGroupReorder(LootFilterGroup draggedGroup, int newIndex)
        {
            if (draggedGroup == null)
                return;

            var currentIndex = _groupList.IndexOf(draggedGroup);
            if (currentIndex == -1 || currentIndex == newIndex)
                return;

            if (newIndex > currentIndex)
                newIndex--;

            XMLogging.WriteLine($"[Filters] Reordering '{draggedGroup.Name}' from index {currentIndex} to {newIndex}");

            _groupList.RemoveAt(currentIndex);
            LootFilterManager.CurrentGroups.Groups.RemoveAt(currentIndex);

            _groupList.Insert(newIndex, draggedGroup);
            LootFilterManager.CurrentGroups.Groups.Insert(newIndex, draggedGroup);

            for (int i = 0; i < _groupList.Count; i++)
            {
                _groupList[i].Index = i;
            }

            OnPropertyChanged(nameof(GroupList));
            lstFilterGroups.SelectedItem = draggedGroup;

            LootFilterManager.Save();

            NotificationsShared.Info($"[Filters] Moved '{draggedGroup.Name}' to priority {newIndex}");
        }
        #endregion

        #region Loot Items Drag and Drop Implementation
        private void GroupedItemsListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(GroupedItemsListView);

            var hitTest = VisualTreeHelper.HitTest(GroupedItemsListView, _dragStartPoint);
            if (hitTest?.VisualHit != null)
            {
                var listViewItem = FindParent<ListViewItem>(hitTest.VisualHit);
                if (listViewItem?.DataContext is GroupedLootFilterEntry item)
                {
                    var hitElement = hitTest.VisualHit as FrameworkElement;
                    var dragHandle = hitElement;

                    while (dragHandle != null)
                    {
                        if ((dragHandle is Border border && border.Cursor == Cursors.SizeAll) ||
                            (dragHandle is System.Windows.Shapes.Path shapePath && FindParent<Border>(shapePath)?.Cursor == Cursors.SizeAll))
                        {
                            _draggedLootItem = item;
                            _draggedLootItemContainer = listViewItem;
                            break;
                        }
                        dragHandle = VisualTreeHelper.GetParent(dragHandle) as FrameworkElement;
                    }
                }
            }
        }

        private void GroupedItemsListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                _draggedLootItem != null &&
                !_isItemDragging)
            {
                var currentPosition = e.GetPosition(GroupedItemsListView);
                var diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isItemDragging = true;
                    var dragData = new DataObject("GroupedLootFilterEntry", _draggedLootItem);

                    if (_draggedLootItemContainer != null)
                        _draggedLootItemContainer.Opacity = 0.5;

                    var result = DragDrop.DoDragDrop(GroupedItemsListView, dragData, DragDropEffects.Move);

                    if (_draggedLootItemContainer != null)
                        _draggedLootItemContainer.Opacity = 1.0;

                    _isItemDragging = false;
                    _draggedLootItem = null;
                    _draggedLootItemContainer = null;
                }
            }
        }

        private void GroupedItemsListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedLootItem = null;
            _draggedLootItemContainer = null;
        }

        private void GroupedItemsListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;

            if (e.Data.GetDataPresent("GroupedLootFilterEntry"))
            {
                e.Effects = DragDropEffects.Move;

                var position = e.GetPosition(GroupedItemsListView);
                _itemInsertIndex = GetInsertIndex(GroupedItemsListView, position);

                UpdateItemDropIndicators(_itemInsertIndex);
            }

            e.Handled = true;
        }

        private void GroupedItemsListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("GroupedLootFilterEntry") &&
                e.Data.GetData("GroupedLootFilterEntry") is GroupedLootFilterEntry draggedItem &&
                _itemInsertIndex >= 0)
            {
                PerformItemReorder(draggedItem, _itemInsertIndex);
            }

            ClearItemDropIndicators();
            _itemInsertIndex = -1;

            e.Handled = true;
        }

        private void UpdateItemDropIndicators(int insertIndex)
        {
            ClearItemDropIndicators();

            if (insertIndex >= 0 && insertIndex < GroupedItemsListView.Items.Count)
            {
                var container = GroupedItemsListView.ItemContainerGenerator.ContainerFromIndex(insertIndex) as ListViewItem;
                if (container != null)
                {
                    var indicator = FindChild<Border>(container, "TopDropIndicator");
                    if (indicator != null)
                        indicator.Visibility = Visibility.Visible;
                }
            }
            else if (insertIndex == GroupedItemsListView.Items.Count && GroupedItemsListView.Items.Count > 0)
            {
                var container = GroupedItemsListView.ItemContainerGenerator.ContainerFromIndex(GroupedItemsListView.Items.Count - 1) as ListViewItem;
                if (container != null)
                {
                    var indicator = FindChild<Border>(container, "BottomDropIndicator");
                    if (indicator != null)
                        indicator.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClearItemDropIndicators()
        {
            for (int i = 0; i < GroupedItemsListView.Items.Count; i++)
            {
                var container = GroupedItemsListView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    var topIndicator = FindChild<Border>(container, "TopDropIndicator");
                    var bottomIndicator = FindChild<Border>(container, "BottomDropIndicator");

                    if (topIndicator != null)
                        topIndicator.Visibility = Visibility.Collapsed;

                    if (bottomIndicator != null)
                        bottomIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void PerformItemReorder(GroupedLootFilterEntry draggedItem, int newIndex)
        {
            if (draggedItem == null || _selectedGroup == null)
                return;

            var currentIndex = _groupItems.IndexOf(draggedItem);
            if (currentIndex == -1 || currentIndex == newIndex)
                return;

            if (newIndex > currentIndex)
                newIndex--;

            XMLogging.WriteLine($"[Filters] Reordering '{draggedItem.Name}' from index {currentIndex} to {newIndex} in group '{_selectedGroup.Name}'");

            _groupItems.RemoveAt(currentIndex);
            _selectedGroup.Items.RemoveAt(currentIndex);

            _groupItems.Insert(newIndex, draggedItem);
            _selectedGroup.Items.Insert(newIndex, draggedItem);

            LootFilterManager.Save();

            NotificationsShared.Info($"[Filters] Moved '{draggedItem.Name}' within group '{_selectedGroup.Name}'");
        }
        #endregion

        #region Shared Drag & Drop Helper Methods
        private int GetInsertIndex(ListView listView, Point position)
        {
            for (int i = 0; i < listView.Items.Count; i++)
            {
                var container = listView.ItemContainerGenerator.ContainerFromIndex(i) as ListViewItem;
                if (container != null)
                {
                    var containerPosition = container.TranslatePoint(new Point(0, 0), listView);
                    var containerBounds = new Rect(containerPosition, container.RenderSize);

                    if (position.Y < containerBounds.Top + containerBounds.Height / 2)
                        return i;
                }
            }

            return listView.Items.Count;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null)
                return null;

            if (parentObject is T parent)
                return parent;

            return FindParent<T>(parentObject);
        }

        private static T FindChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null)
                return null;

            T foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    if (string.IsNullOrEmpty(childName) ||
                        (child is FrameworkElement fe && fe.Name == childName))
                    {
                        foundChild = typedChild;
                        break;
                    }
                }
                else
                {
                    foundChild = FindChild<T>(child, childName);
                    if (foundChild != null)
                        break;
                }
            }

            return foundChild;
        }
        #endregion

        #region Events
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

        private void btnCloseHeader_Click(object sender, RoutedEventArgs e)
        {
            _openColorPicker?.Close();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup != null)
                _selectedGroup.PropertyChanged -= OnGroupChanged;

            foreach (var item in _groupItems)
                item.PropertyChanged -= OnItemChanged;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion
        #endregion

        #region Loot Filter Settings
        #region Functions/Methods
        private void RegisterSettingsEvents()
        {
            // Loot Filter Settings
            btnMenu.Click += LootFilterButton_Click;
            mnuExportSelected.Click += LootFilterMenuItem_Click;
            mnuExportAll.Click += LootFilterMenuItem_Click;
            mnuImport.Click += LootFilterMenuItem_Click;

            lstFilterGroups.SelectionChanged += lstFilterGroups_SelectionChanged;
            btnRemoveGroup.Click += LootFilterButton_Click;
            btnAddGroup.Click += LootFilterButton_Click;

            chkStatic.Checked += LootFilterCheckbox_CheckChanged;
            chkStatic.Unchecked += LootFilterCheckbox_CheckChanged;
            chkNotify.Checked += LootFilterCheckbox_CheckChanged;
            chkNotify.Unchecked += LootFilterCheckbox_CheckChanged;

            nudNotifyTime.ValueChanged += LootFilterNumericUpDown_ValueChanged;
            txtGroupName.TextChanged += LootFilterTextBox_TextChanged;

            // Loot Filter Items
            btnAddItem.Click += LootFilterButton_Click;
            btnRemoveItem.Click += LootFilterButton_Click;
            txtItemSearch.TextChanged += LootFilterTextBox_TextChanged;

            // Bulk Actions
            btnBulkColor.Click += LootFilterButton_Click;
            btnBulkEnable.Click += LootFilterButton_Click;
            btnBulkDisable.Click += LootFilterButton_Click;
            btnBulkNotify.Click += LootFilterButton_Click;

            GroupedItemsListView.SelectionChanged += GroupedItemsListView_SelectionChanged;
        }

        private void LoadSettings()
        {
            LootFilterManager.Load();

            _groupList.Clear();

            foreach (var group in LootFilterManager.CurrentGroups.Groups.OrderBy(g => g.Index))
                _groupList.Add(group);

            if (_groupList.Count == 0)
                EnsureDefaultLootFilter();

            UpdateUIControls();

            if (_groupList.Count > 0)
            {
                SetSelectedGroup(_groupList[0]);

                Dispatcher.BeginInvoke(() =>
                {
                    lstFilterGroups.SelectedItem = _groupList[0];
                    lstFilterGroups.Focus();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }

            if (cboItems.Items.Count < 1)
                SearchForItem();
        }

        /// <summary>
        /// Ensures at least one filter group exists.
        /// </summary>
        private void EnsureDefaultLootFilter()
        {
            if (_groupList.Count == 0)
            {
                XMLogging.WriteLine("[Filters] Creating default filter group.");

                var defaultGroup = new LootFilterGroup
                {
                    Name = DEFAULT_GROUP_NAME,
                    Index = 0,
                    Enabled = true,
                    IsStatic = true,
                    Items = new List<GroupedLootFilterEntry>()
                };

                _groupList.Add(defaultGroup);

                LootFilterManager.CurrentGroups.Groups.Add(defaultGroup);
                LootFilterManager.Save();

                SetSelectedGroup(defaultGroup);

                NotificationsShared.Info("[Filters] Created default filter group.");
            }
        }

        private void SetSelectedGroup(LootFilterGroup group)
        {
            if (_selectedGroup != null)
            {
                _selectedGroup.PropertyChanged -= OnGroupChanged;

                foreach (var item in _groupItems)
                    item.PropertyChanged -= OnItemChanged;
            }

            _selectedGroup = group;

            if (_selectedGroup != null)
            {
                _selectedGroup.PropertyChanged += OnGroupChanged;
                _groupItems = new ObservableCollection<GroupedLootFilterEntry>(_selectedGroup.Items);

                foreach (var item in _groupItems)
                    item.PropertyChanged += OnItemChanged;

                GroupedItemsListView.ItemsSource = _groupItems;
                _groupItemsView = CollectionViewSource.GetDefaultView(_groupItems);

                UpdateUIControls();
            }
        }

        private void UpdateUIControls()
        {
            if (_selectedGroup == null)
                return;

            _isUpdatingControls = true;

            try
            {
                txtGroupName.Text = _selectedGroup.Name;
                chkStatic.IsChecked = _selectedGroup.IsStatic;
                chkNotify.IsChecked = _selectedGroup.Notify;
                nudNotifyTime.Value = _selectedGroup.NotTime;
            }
            finally
            {
                _isUpdatingControls = false;
            }
        }

        private void AddGroup()
        {
            var groupName = txtNewGroupName.Text.Trim();

            if (string.IsNullOrWhiteSpace(groupName))
            {
                NotificationsShared.Error("[Filters] Please enter a group name.");
                return;
            }

            if (_groupList.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationsShared.Error("[Filters] A group with this name already exists.");
                return;
            }

            var index = _groupList.Count == 0 ? 0 : _groupList.Max(g => g.Index) + 1;
            var newGroup = new LootFilterGroup
            {
                Index = index,
                Name = groupName,
                Enabled = true,
                IsStatic = true
            };

            _groupList.Add(newGroup);
            LootFilterManager.CurrentGroups.Groups.Add(newGroup);
            LootFilterManager.Save();

            txtNewGroupName.Text = "";

            OnPropertyChanged(nameof(GroupList));
            lstFilterGroups.SelectedItem = newGroup;
        }

        public void RemoveGroup()
        {
            if (_selectedGroup == null)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to remove the group '{_selectedGroup.Name}'?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var groupToRemove = _selectedGroup;

                if (_groupList.Count > 1)
                {
                    int currentIndex = lstFilterGroups.SelectedIndex;
                    int newIndex = (currentIndex > 0) ? currentIndex - 1 : 0;

                    if (currentIndex == 0 && _groupList.Count > 1)
                        newIndex = 1;

                    lstFilterGroups.SelectedIndex = newIndex;
                }

                _groupList.Remove(groupToRemove);
                LootFilterManager.CurrentGroups.Groups.Remove(groupToRemove);

                if (_groupList.Count == 0)
                    EnsureDefaultLootFilter();

                RenumberGroupIndices();
                LootFilterManager.Save();

                OnPropertyChanged(nameof(GroupList));

                if (_groupList.Count > 0 && lstFilterGroups.SelectedItem == null)
                    lstFilterGroups.SelectedIndex = 0;
            }
        }

        private void ToggleStatic()
        {
            if (_isUpdatingControls || _selectedGroup == null)
                return;

            _selectedGroup.IsStatic = chkStatic.IsChecked ?? true;
            SaveLootFilter();
        }

        private void ToggleNotifications()
        {
            if (_isUpdatingControls || _selectedGroup == null)
                return;

            _selectedGroup.Notify = chkNotify.IsChecked ?? false;
            SaveLootFilter();
        }

        private void RenumberGroupIndices()
        {
            for (int i = 0; i < _groupList.Count; i++)
            {
                _groupList[i].Index = i;
            }
        }

        private void AddItemToGroup()
        {
            if (_selectedGroup == null || cboItems.SelectedValue is not string itemID)
                return;

            var itemName = EftDataManager.AllItems.TryGetValue(itemID, out var item) ? item.ShortName : cboItems.Text;
            var existsInGroup = _selectedGroup.Items.Any(i => i.ItemID == itemID);
            var otherGroupWithItem = LootFilterManager.CurrentGroups.Groups
                .FirstOrDefault(g => g.Name != _selectedGroup.Name && g.Items.Any(i => i.ItemID == itemID));

            if (existsInGroup)
            {
                NotificationsShared.Info($"[Filters] {itemName} already exists in the current group: {_selectedGroup.Name}");
                return;
            }

            if (otherGroupWithItem != null)
            {
                var result = MessageBox.Show(
                    $"{itemName} already exists in another group: {otherGroupWithItem.Name}. Add it anyway?",
                    "Item Already Exists",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                    return;
            }

            NotificationsShared.Info($"[Filters] Adding {itemName} to group: {_selectedGroup.Name}");

            var entry = new GroupedLootFilterEntry
            {
                ItemID = itemID,
                Enabled = true,
                Color = "#FF2CF243",
                IsStatic = _selectedGroup.IsStatic,
                Note = "",
                Blacklist = false
            };

            _selectedGroup.Items.Add(entry);
            _groupItems.Add(entry);

            entry.PropertyChanged += OnItemChanged;

            LootFilterManager.Save();

            cboItems.SelectedIndex = -1;
            txtItemSearch.Text = "";
        }

        public void RemoveItemFromGroup()
        {
            if (_selectedGroup == null)
                return;

            var selectedItems = GroupedItemsListView.SelectedItems.Cast<GroupedLootFilterEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                NotificationsShared.Warning("[Filters] Please select one or more items to remove");
                return;
            }

            var itemNames = selectedItems.Count == 1
                ? $"'{selectedItems[0].Name}'"
                : $"{selectedItems.Count} items";

            var result = MessageBox.Show(
                $"Are you sure you want to remove {itemNames} from this group?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var selectedEntry in selectedItems)
                {
                    selectedEntry.PropertyChanged -= OnItemChanged;
                    _groupItems.Remove(selectedEntry);
                }

                _selectedGroup.Items.Clear();
                _selectedGroup.Items.AddRange(_groupItems);

                LootFilterManager.Save();

                var message = selectedItems.Count == 1
                    ? $"[Filters] Removed {selectedItems[0].Name} from group: {_selectedGroup.Name}"
                    : $"[Filters] Removed {selectedItems.Count} items from group: {_selectedGroup.Name}";

                NotificationsShared.Info(message);
            }
        }
        public bool HandleDeleteKey()
        {
            if (GroupedItemsListView.IsKeyboardFocusWithin &&
                GroupedItemsListView.SelectedItems?.Count > 0)
            {
                RemoveItemFromGroup();
                return true;
            }
            if (lstFilterGroups.IsKeyboardFocusWithin && _selectedGroup != null)
            {
                RemoveGroup();
                return true;
            }
            if (GroupedItemsListView.SelectedItems?.Count > 0)
            {
                RemoveItemFromGroup();
                return true;
            }
            if (_selectedGroup != null)
            {
                RemoveGroup();
                return true;
            }

            return false;
        }
        
        private void SearchForItem()
        {
            var query = txtItemSearch.Text.ToLowerInvariant();
            var filteredItems = EftDataManager.AllItems?
                                .Where(kv => kv.Value?.Name?.ToLowerInvariant().Contains(query) == true)
                                .OrderBy(kv => kv.Value?.Name)
                                .Take(10)
                                .Select(kv => new { Id = kv.Key, Name = kv.Value?.Name ?? "NULL" })
                                .ToList();

            cboItems.DisplayMemberPath = "Name";
            cboItems.SelectedValuePath = "Id";
            cboItems.ItemsSource = filteredItems;

            if (filteredItems != null && filteredItems.Count > 0)
                cboItems.SelectedIndex = 0;

            if (_groupItemsView != null)
            {
                _groupItemsView.Filter = item => item is GroupedLootFilterEntry entry && (string.IsNullOrWhiteSpace(query) || entry.Name.ToLowerInvariant().Contains(query));
                _groupItemsView.Refresh();
            }
        }

        private void EditGroupName()
        {
            if (_isUpdatingControls || _selectedGroup == null)
                return;

            _selectedGroup.Name = txtGroupName.Text;

            SaveLootFilter();

            var selectedItem = lstFilterGroups.SelectedItem;
            lstFilterGroups.Items.Refresh();
            lstFilterGroups.SelectedItem = selectedItem;
        }

        private void EditNotificationTime(int value)
        {
            if (_isUpdatingControls || _selectedGroup == null)
                return;

            _selectedGroup.NotTime = value;

            SaveLootFilter();
        }

        private void RefreshAllLootItems() => LootItem.ClearPaintCache();

        private void SaveLootFilter()
        {
            if (_selectedGroup != null)
            {
                _selectedGroup.Items.Clear();
                _selectedGroup.Items.AddRange(_groupItems);
                LootItem.ClearPaintCache();
                LootFilterManager.Save();
            }
        }

        private void RefreshGroupsListView(LootFilterGroup selectedGroup = null)
        {
            if (selectedGroup == null)
                selectedGroup = lstFilterGroups.SelectedItem as LootFilterGroup;

            OnPropertyChanged(nameof(GroupList));

            if (selectedGroup != null && _groupList.Contains(selectedGroup))
                lstFilterGroups.SelectedItem = selectedGroup;
            else if (_groupList.Count > 0)
                lstFilterGroups.SelectedIndex = 0;
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

        private void ExportSelectedFilter()
        {
            if (_selectedGroup == null)
            {
                NotificationsShared.Warning("[Filters] No filter selected for export.");
                return;
            }

            try
            {
                var jsonData = JsonSerializer.Serialize(new { groups = new List<LootFilterGroup> { _selectedGroup } },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                    });

                Clipboard.SetText(jsonData);
                NotificationsShared.Success($"[Filters] Exported '{_selectedGroup.Name}' to clipboard");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"[Filters] Failed to export filter to clipboard: {ex.Message}");
                XMLogging.WriteLine($"[Filters] Export error: {ex}");
            }
        }

        private void ExportAllFilters()
        {
            if (_groupList.Count == 0)
            {
                NotificationsShared.Warning("[Filters] No filters to export.");
                return;
            }

            try
            {
                var exportData = new
                {
                    groups = _groupList.ToList(),
                    exportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    totalGroups = _groupList.Count
                };

                var jsonData = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                });

                Clipboard.SetText(jsonData);
                NotificationsShared.Success($"[Filters] Exported {_groupList.Count} filters to clipboard");
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"[Filters] Failed to export filters to clipboard: {ex.Message}");
                XMLogging.WriteLine($"[Filters] Export error: {ex}");
            }
        }

        private void ImportFiltersFromClipboard()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    NotificationsShared.Warning("[Filters] Clipboard does not contain text data.");
                    return;
                }

                var clipboardText = Clipboard.GetText();
                var importData = JsonSerializer.Deserialize<dynamic>(clipboardText);

                JsonElement groupsElement;
                List<LootFilterGroup> groups = null;

                if (importData is JsonElement element && element.TryGetProperty("groups", out groupsElement))
                {
                    groups = JsonSerializer.Deserialize<List<LootFilterGroup>>(groupsElement.GetRawText());
                }
                else
                {
                    try
                    {
                        groups = JsonSerializer.Deserialize<List<LootFilterGroup>>(clipboardText);
                    }
                    catch
                    {
                        NotificationsShared.Error("[Filters] Invalid filter data in clipboard.");
                        return;
                    }
                }

                if (groups == null || groups.Count == 0)
                {
                    NotificationsShared.Error("[Filters] No valid filter groups found in clipboard.");
                    return;
                }

                var result = MessageBox.Show(
                    $"Found {groups.Count} filter group(s) in clipboard.\n\n" +
                    "Do you want to:\n" +
                    "YES: Add these filters to your existing ones\n" +
                    "NO: Replace all existing filters with the imported ones\n" +
                    "CANCEL: Cancel the import operation",
                    "Import Options",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                var addedCount = 0;
                var replacedCount = 0;

                if (result == MessageBoxResult.No)
                {
                    _groupList.Clear();
                    LootFilterManager.CurrentGroups.Groups.Clear();

                    foreach (var group in groups)
                    {
                        group.Index = _groupList.Count;
                        _groupList.Add(group);
                        LootFilterManager.CurrentGroups.Groups.Add(group);
                        replacedCount++;
                    }
                }
                else
                {
                    foreach (var importedGroup in groups)
                    {
                        var existingGroup = _groupList.FirstOrDefault(g => g.Name.Equals(importedGroup.Name, StringComparison.OrdinalIgnoreCase));

                        if (existingGroup != null)
                        {
                            var replaceResult = MessageBox.Show(
                                $"A filter group named '{importedGroup.Name}' already exists.\n" +
                                "Do you want to replace it?",
                                "Duplicate Filter Group",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (replaceResult == MessageBoxResult.Yes)
                            {
                                importedGroup.Index = existingGroup.Index;
                                var index = _groupList.IndexOf(existingGroup);
                                _groupList[index] = importedGroup;
                                LootFilterManager.CurrentGroups.Groups[index] = importedGroup;
                                replacedCount++;
                            }
                        }
                        else
                        {
                            importedGroup.Index = _groupList.Count == 0 ? 0 : _groupList.Max(g => g.Index) + 1;
                            _groupList.Add(importedGroup);
                            LootFilterManager.CurrentGroups.Groups.Add(importedGroup);
                            addedCount++;
                        }
                    }
                }

                if (_groupList.Count == 0)
                    EnsureDefaultLootFilter();

                RefreshGroupsListView();
                RenumberGroupIndices();
                LootFilterManager.Save();

                var message = result == MessageBoxResult.No
                    ? $"[Filters] Replaced all filters. Imported {replacedCount} group(s)."
                    : $"[Filters] Added {addedCount} new group(s), replaced {replacedCount} existing group(s).";

                NotificationsShared.Success(message);
            }
            catch (Exception ex)
            {
                NotificationsShared.Error($"[Filters] Failed to import filters from clipboard: {ex.Message}");
                XMLogging.WriteLine($"[Filters] Import error: {ex}");
            }
        }

        #region Bulk Operations
        private void BulkChangeColor()
        {
            var selectedItems = GroupedItemsListView.SelectedItems.Cast<GroupedLootFilterEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                NotificationsShared.Warning("[Filters] Please select one or more items to change color");
                return;
            }

            _openColorPicker?.Close();

            var firstItemColor = (Color)ColorConverter.ConvertFromString(selectedItems[0].Color);
            var picker = SingleOpenHelper.CreateControl<ColorPicker>();
            picker.SelectedBrush = new SolidColorBrush(firstItemColor);
            var window = new PopupWindow
            {
                PopupElement = picker,
                AllowsTransparency = true,
                WindowStyle = WindowStyle.None,
                MinWidth = 0,
                MinHeight = 0
            };

            _openColorPicker = window;

            var parentWindow = MainWindow.GetWindow(this);

            void UpdatePickerPosition()
            {
                try
                {
                    var btnPos = btnBulkColor.PointToScreen(new Point(0, 0));
                    var leftPos = btnPos.X + btnBulkColor.ActualWidth - 5;
                    var topPos = btnPos.Y - btnBulkColor.ActualHeight - 5;

                    window.Left = leftPos;
                    window.Top = topPos;
                }
                catch { }
            }

            EventHandler parentLocationChanged = (s, e) => UpdatePickerPosition();
            SizeChangedEventHandler parentSizeChanged = (s, e) => UpdatePickerPosition();

            if (parentWindow != null)
            {
                parentWindow.LocationChanged += parentLocationChanged;
                parentWindow.SizeChanged += parentSizeChanged;
            }

            picker.Confirmed += (s, args) =>
            {
                if (picker.SelectedBrush is SolidColorBrush scb)
                {
                    foreach (var item in selectedItems)
                    {
                        item.Color = scb.Color.ToString();
                    }
                    SaveLootFilter();

                    var message = selectedItems.Count == 1
                        ? $"[Filters] Changed color for {selectedItems[0].Name}"
                        : $"[Filters] Changed color for {selectedItems.Count} items";
                    NotificationsShared.Info(message);
                }
                window.Close();
            };

            picker.Canceled += (s, args) =>
            {
                window.Close();
            };

            window.Loaded += (s, args) =>
            {
                UpdatePickerPosition();
            };

            window.Closed += (s, args) =>
            {
                _openColorPicker = null;

                if (parentWindow != null)
                {
                    parentWindow.LocationChanged -= parentLocationChanged;
                    parentWindow.SizeChanged -= parentSizeChanged;
                }
            };

            window.Show(btnBulkColor, false);
        }

        private void BulkEnable()
        {
            var selectedItems = GroupedItemsListView.SelectedItems.Cast<GroupedLootFilterEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                NotificationsShared.Warning("[Filters] Please select one or more items to enable");
                return;
            }

            foreach (var item in selectedItems)
            {
                item.Enabled = true;
            }

            SaveLootFilter();

            var message = selectedItems.Count == 1
                ? $"[Filters] Enabled {selectedItems[0].Name}"
                : $"[Filters] Enabled {selectedItems.Count} items";
            NotificationsShared.Info(message);
        }

        private void BulkDisable()
        {
            var selectedItems = GroupedItemsListView.SelectedItems.Cast<GroupedLootFilterEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                NotificationsShared.Warning("[Filters] Please select one or more items to disable");
                return;
            }

            foreach (var item in selectedItems)
            {
                item.Enabled = false;
            }

            SaveLootFilter();

            var message = selectedItems.Count == 1
                ? $"[Filters] Disabled {selectedItems[0].Name}"
                : $"[Filters] Disabled {selectedItems.Count} items";
            NotificationsShared.Info(message);
        }

        private void BulkToggleNotify()
        {
            var selectedItems = GroupedItemsListView.SelectedItems.Cast<GroupedLootFilterEntry>().ToList();

            if (selectedItems.Count == 0)
            {
                NotificationsShared.Warning("[Filters] Please select one or more items to toggle notifications");
                return;
            }

            var anyDisabled = selectedItems.Any(x => !x.Notify);
            var newNotifyState = anyDisabled;

            foreach (var item in selectedItems)
            {
                item.Notify = newNotifyState;
            }

            SaveLootFilter();

            var action = newNotifyState ? "enabled" : "disabled";
            var message = selectedItems.Count == 1
                ? $"[Filters] {action} notifications for {selectedItems[0].Name}"
                : $"[Filters] {action} notifications for {selectedItems.Count} items";
            NotificationsShared.Info(message);
        }
        #endregion

        private void EditItemNote(GroupedLootFilterEntry entry)
        {
            var result = TextInputWindow.ShowMultiLine(
                owner: Window.GetWindow(this),
                title: $"Note for {entry.Name}",
                prompt: "Add a personal note for this item:",
                defaultText: entry.Note ?? "",
                placeholder: "Enter your note here...",
                width: 450,
                height: 70
            );

            if (result != null && result != entry.Note)
            {
                var oldNote = entry.Note;
                entry.Note = result;
                SaveLootFilter();

                if (string.IsNullOrWhiteSpace(result) && !string.IsNullOrWhiteSpace(oldNote))
                {
                    NotificationsShared.Info($"[Filters] Cleared note for {entry.Name}");
                }
                else if (!string.IsNullOrWhiteSpace(result))
                {
                    var action = string.IsNullOrWhiteSpace(oldNote) ? "Added" : "Updated";
                    NotificationsShared.Info($"[Filters] {action} note for {entry.Name}");
                }
            }
        }

        public static LootFilterGroup CreateWeaponAmmoGroup()
        {
            XMLogging.WriteLine("[Filters] Creating dynamic group for current weapon ammo.");

            var groupName = Memory?.LocalPlayer.Hands.CurrentItem;
            var weaponBsgId = Memory?.LocalPlayer.Hands.CurrentItemId;

            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(weaponBsgId))
            {
                XMLogging.WriteLine("[Filters] Weapon name or ID is null. Skipping group creation.");
                return null;
            }

            if (!EftDataManager.AllItems.TryGetValue(weaponBsgId, out var weaponItem) ||
                string.IsNullOrEmpty(weaponItem.Caliber))
            {
                XMLogging.WriteLine($"[Filters] Caliber not found for weapon ID: {weaponBsgId}");
                return null;
            }

            var caliber = weaponItem.Caliber;
            var matchingAmmoIds = AmmoLookup.GetCompatibleAmmo(caliber);

            if (LootFilterManager.CurrentGroups.Groups.Any(g => g.Name == groupName))
            {
                XMLogging.WriteLine($"[Filters] Group '{groupName}' already exists. Skipping creation.");
                return null;
            }

            var dynamicGroup = new LootFilterGroup
            {
                Name = groupName,
                Index = LootFilterManager.CurrentGroups.Groups.Count == 0
                    ? 0
                    : LootFilterManager.CurrentGroups.Groups.Max(g => g.Index) + 1,
                Enabled = true,
                IsStatic = false,
                Items = matchingAmmoIds
                    .Where(id => EftDataManager.AllItems.ContainsKey(id))
                    .Select(id => new GroupedLootFilterEntry
                    {
                        ItemID = id,
                        Enabled = true,
                        Color = "#FF2CF243",
                        IsStatic = false
                    })
                    .ToList()
            };

            MainWindow.Window.LootFilterControl._groupList.Add(dynamicGroup);
            LootFilterManager.CurrentGroups.Groups.Add(dynamicGroup);

            LootFilterManager.Save();

            var lootFilterSettings = MainWindow.Window.LootFilterControl;
            lootFilterSettings.RefreshGroupsListView(dynamicGroup);

            XMLogging.WriteLine($"[Filters] Created dynamic group: {groupName} with {dynamicGroup.Items.Count} items.");
            return dynamicGroup;
        }

        public static void RemoveNonStaticGroups()
        {
            var lootFilterSettings = MainWindow.Window.LootFilterControl;
            var nonStaticGroups = LootFilterManager.CurrentGroups.Groups
                .Where(g => g.IsStatic == false)
                .ToList();

            var needsRefresh = false;
            var selectedWillBeRemoved = lootFilterSettings._selectedGroup != null &&
                                        !lootFilterSettings._selectedGroup.IsStatic;

            foreach (var group in nonStaticGroups)
            {
                lootFilterSettings._groupList.Remove(group);
                LootFilterManager.CurrentGroups.Groups.Remove(group);
                XMLogging.WriteLine($"[Filters] Removed non-static group: {group.Name}");
                needsRefresh = true;
            }

            XMLogging.WriteLine($"[Filters] Removed {nonStaticGroups.Count} non-static groups.");
            firstRemove = true;

            if (needsRefresh)
            {
                if (lootFilterSettings._groupList.Count == 0)
                {
                    lootFilterSettings.EnsureDefaultLootFilter();
                }
                else if (selectedWillBeRemoved)
                {
                    lootFilterSettings.lstFilterGroups.SelectedIndex = 0;
                }

                lootFilterSettings.RenumberGroupIndices();
                LootFilterManager.Save();
                lootFilterSettings.RefreshGroupsListView();
            }
        }

        public static Predicate<LootItem> Create()
        {
            var search = SearchString?.Trim();
            var usePrices = string.IsNullOrEmpty(search);

            if (usePrices)
            {
                Predicate<LootItem> p = (x) =>
                {
                    if (x.IsGroupedBlacklisted)
                        return false;

                    return (x.IsRegularLoot || x.IsValuableLoot || x.IsImportant || x.IsWishlisted) ||
                            (ShowQuestItems && x.IsQuestCondition) ||
                            (ShowBackpacks && x.IsBackpack) ||
                            (ShowMeds && x.IsMeds) ||
                            (ShowFood && x.IsFood) ||
                            (ShowWeapons && x.IsWeapon);
                };

                return (item) =>
                {
                    if (item is LootAirdrop || item is LootCorpse)
                        return true;

                    if (p(item))
                    {
                        if (item is LootContainer container)
                            container.SetFilter(p);

                        return true;
                    }

                    return false;
                };
            }
            else
            {
                var names = search?.Split(',').Select(a => a.Trim()).ToArray() ?? Array.Empty<string>();

                Predicate<LootItem> p = (x) =>
                {
                    if (x.IsGroupedBlacklisted)
                        return false;

                    return names.Any(a => x.Name.Contains(a, StringComparison.OrdinalIgnoreCase));
                };

                return (item) =>
                {
                    if (item is LootAirdrop || item.ContainsSearchPredicate(p))
                    {
                        if (item is LootContainer container)
                            container.SetFilter(p);

                        return true;
                    }

                    return false;
                };
            }
        }
        #endregion

        #region Events
        private void LootFilterCheckbox_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is string tag)
            {
                switch (tag)
                {
                    case "StaticFilter":
                        ToggleStatic();
                        break;
                    case "FilterNotifications":
                        ToggleNotifications();
                        break;
                }
            }
        }

        private void LootFilterButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "ContextMenu":
                        OpenContextMenu();
                        break;
                    case "RemoveGroup":
                        RemoveGroup();
                        break;
                    case "AddGroup":
                        AddGroup();
                        break;
                    case "AddItem":
                        AddItemToGroup();
                        break;
                    case "RemoveItem":
                        RemoveItemFromGroup();
                        break;
                    case "BulkColor":
                        BulkChangeColor();
                        break;
                    case "BulkEnable":
                        BulkEnable();
                        break;
                    case "BulkDisable":
                        BulkDisable();
                        break;
                    case "BulkNotify":
                        BulkToggleNotify();
                        break;
                }
            }
        }

        private void LootFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is HandyControl.Controls.TextBox txt && txt.Tag is string tag)
            {
                switch (tag)
                {
                    case "ItemSearch":
                        SearchForItem();
                        break;
                    case "GroupName":
                        EditGroupName();
                        break;
                }
            }
        }

        private void LootFilterNumericUpDown_ValueChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (sender is HandyControl.Controls.NumericUpDown nud && nud.Tag is string tag)
            {
                var value = (int)e.Info;

                switch (tag)
                {
                    case "NotificationTime":
                        EditNotificationTime(value);
                        break;
                }
            }
        }

        private void lstFilterGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingControls || lstFilterGroups.SelectedItem == null)
                return;

            var selectedGroup = lstFilterGroups.SelectedItem as LootFilterGroup;

            if (selectedGroup != null)
            {
                XMLogging.WriteLine($"[Filters] Selected group changed to: {selectedGroup.Name}, Index: {selectedGroup.Index}");
                SetSelectedGroup(selectedGroup);
            }
        }

        private void GroupedItemsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var hasSelection = GroupedItemsListView.SelectedItems.Count > 0;
            var selectionCount = GroupedItemsListView.SelectedItems.Count;

            btnBulkColor.IsEnabled = hasSelection;
            btnBulkEnable.IsEnabled = hasSelection;
            btnBulkDisable.IsEnabled = hasSelection;
            btnBulkNotify.IsEnabled = hasSelection;

            if (selectionCount > 0)
            {
                btnBulkColor.ToolTip = $"Change color of {selectionCount} selected item{(selectionCount > 1 ? "s" : "")}";
                btnBulkEnable.ToolTip = $"Enable {selectionCount} selected item{(selectionCount > 1 ? "s" : "")}";
                btnBulkDisable.ToolTip = $"Disable {selectionCount} selected item{(selectionCount > 1 ? "s" : "")}";
                btnBulkNotify.ToolTip = $"Toggle notifications for {selectionCount} selected item{(selectionCount > 1 ? "s" : "")}";
            }
            else
            {
                btnBulkColor.ToolTip = "Change color of selected items";
                btnBulkEnable.ToolTip = "Enable selected items";
                btnBulkDisable.ToolTip = "Disable selected items";
                btnBulkNotify.ToolTip = "Toggle notifications for selected items";
            }
        }

        private void ColorPicker_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GroupedLootFilterEntry entry)
            {
                _openColorPicker?.Close();

                var originalColor = (Color)ColorConverter.ConvertFromString(entry.Color);
                var picker = SingleOpenHelper.CreateControl<ColorPicker>();
                picker.SelectedBrush = new SolidColorBrush(originalColor);
                var window = new PopupWindow
                {
                    PopupElement = picker,
                    AllowsTransparency = true,
                    WindowStyle = WindowStyle.None,
                    MinWidth = 0,
                    MinHeight = 0
                };

                _openColorPicker = window;

                var parentWindow = MainWindow.GetWindow(btn);
                var lootFilterPanel = parentWindow?.FindName("LootFilterPanel") as FrameworkElement;

                void UpdatePickerPosition()
                {
                    try
                    {
                        var buttonPos = btn.PointToScreen(new Point(0, 0));
                        var leftPos = buttonPos.X + btn.ActualWidth - 5;
                        var topPos = buttonPos.Y - btn.ActualHeight - 5;

                        window.Left = leftPos;
                        window.Top = topPos;
                    }
                    catch { }
                }

                EventHandler parentLocationChanged = (s, e) => UpdatePickerPosition();
                SizeChangedEventHandler parentSizeChanged = (s, e) => UpdatePickerPosition();
                EventHandler panelLayoutUpdated = (s, e) => UpdatePickerPosition();

                if (parentWindow != null)
                {
                    parentWindow.LocationChanged += parentLocationChanged;
                    parentWindow.SizeChanged += parentSizeChanged;
                }

                if (lootFilterPanel != null)
                {
                    lootFilterPanel.LayoutUpdated += panelLayoutUpdated;
                }

                picker.Confirmed += (s, args) =>
                {
                    if (picker.SelectedBrush is SolidColorBrush scb)
                    {
                        entry.Color = scb.Color.ToString();
                        btn.Background = scb;
                        SaveLootFilter();
                    }
                    window.Close();
                };

                picker.Canceled += (s, args) =>
                {
                    window.Close();
                };

                window.Loaded += (s, args) =>
                {
                    UpdatePickerPosition();
                };

                window.Closed += (s, args) =>
                {
                    _openColorPicker = null;

                    if (parentWindow != null)
                    {
                        parentWindow.LocationChanged -= parentLocationChanged;
                        parentWindow.SizeChanged -= parentSizeChanged;
                    }

                    if (lootFilterPanel != null)
                    {
                        lootFilterPanel.LayoutUpdated -= panelLayoutUpdated;
                    }
                };

                window.Show(btn, false);
            }
        }

        private void OnGroupChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveLootFilter();
            RefreshAllLootItems();
        }

        private void OnItemChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveLootFilter();
            RefreshAllLootItems();
        }

        private void NotifyIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is GroupedLootFilterEntry item)
            {
                item.Notify = !item.Notify;
                e.Handled = true;
            }
        }

        private void EnabledIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is GroupedLootFilterEntry item)
            {
                item.Enabled = !item.Enabled;
                e.Handled = true;
            }
        }

        private void LootFilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mnu && mnu.Tag is string tag)
            {
                switch (tag)
                {
                    case "ExportSelectedFilter":
                        ExportSelectedFilter();
                        break;
                    case "ExportAllFilters":
                        ExportAllFilters();
                        break;
                    case "ImportFilters":
                        ImportFiltersFromClipboard();
                        break;
                }
            }
        }

        private void GroupStatusIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is LootFilterGroup group)
            {
                group.Enabled = !group.Enabled;

                SaveLootFilter();

                var status = group.Enabled ? "enabled" : "disabled";
                NotificationsShared.Info($"[Filters] {status} group: {group.Name}");

                e.Handled = true;
            }
        }

        private void BlacklistButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GroupedLootFilterEntry entry)
            {
                entry.Blacklist = !entry.Blacklist;
                SaveLootFilter();

                var status = entry.Blacklist ? "blacklisted" : "removed from blacklist";
                NotificationsShared.Info($"[Filters] {entry.Name} {status}");
            }
        }

        private void NoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GroupedLootFilterEntry entry)
                EditItemNote(entry);
        }
        #endregion
        #endregion
    }
}