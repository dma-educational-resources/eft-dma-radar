#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using eft_dma_radar.Tarkov.QuestPlanner;
using eft_dma_radar.UI.Misc;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MenuItem = System.Windows.Controls.MenuItem;

namespace eft_dma_radar.UI.Pages;

/// <summary>
/// Code-behind for QuestPlannerControl.
/// Handles ViewModel lifecycle, accordion expand/collapse,
/// and panel drag/resize/close events.
/// </summary>
public partial class QuestPlannerControl : UserControl
{
    // Events for MainWindow panel system
    public event EventHandler? CloseRequested;
    public event EventHandler? BringToFrontRequested;
    public event EventHandler<PanelDragEventArgs>? DragRequested;
    public event EventHandler<PanelResizeEventArgs>? ResizeRequested;

    private readonly QuestPlannerViewModel _vm;
    private readonly HashSet<int> _expandedRows = new();
    private bool _allMapsExpanded = true;
    private Point _dragStart;

    public QuestPlannerControl()
    {
        InitializeComponent();
        _vm = new QuestPlannerViewModel();
        DataContext = _vm;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuestPlannerViewModel.CurrentSummary))
        {
            // Reset expand state on any summary change (raid end, state transition, etc.)
            _expandedRows.Clear();
            // Defer until after ItemsControl has rendered its containers
            Dispatcher.BeginInvoke(UpdateRowVisibility, System.Windows.Threading.DispatcherPriority.Loaded);
            UpdateStatusBanner();
        }
        else if (e.PropertyName == nameof(QuestPlannerViewModel.ConnectionState)
                 || e.PropertyName == nameof(QuestPlannerViewModel.StatusText)
                 || e.PropertyName == nameof(QuestPlannerViewModel.IsStale))
        {
            UpdateStatusBanner();
        }
        // Banner visibility is handled by XAML DataTriggers, no code-behind needed
    }

    private void UpdateStatusBanner()
    {
        var state = _vm.ConnectionState;
        var isStale = _vm.IsStale;

        if (state == QuestConnectionState.Disconnected)
        {
            var hasLastPlan = _vm.CurrentSummary != null;
            StatusBannerText.Text = hasLastPlan
                ? "Disconnected - showing last known plan"
                : "Quest plan will appear when connected in lobby.";
            StatusBanner.Visibility = Visibility.Visible;
        }
        else if (state == QuestConnectionState.Lobby && isStale)
        {
            StatusBannerText.Text = "Could not refresh quest data - showing last known plan";
            StatusBanner.Visibility = Visibility.Visible;
        }
        else
        {
            StatusBanner.Visibility = Visibility.Collapsed;
        }
    }

    // Accordion: MapRow_Click is wired via Tag holding the row index
    private void MapRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is int idx)
        {
            if (_expandedRows.Contains(idx))
                _expandedRows.Remove(idx);
            else
                _expandedRows.Add(idx);
            UpdateRowVisibility();
        }
    }

    private void UpdateRowVisibility()
    {
        // Each content panel is tagged with its index in the ItemsControl
        foreach (var item in GetMapContentPanels())
        {
            if (item.Tag is int idx)
                item.Visibility = _expandedRows.Contains(idx) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private IEnumerable<FrameworkElement> GetMapContentPanels()
    {
        // Walk MapListControl items and find StackPanels with Tag set as int (content panels)
        return FindVisualChildren<StackPanel>(MapListControl)
            .Where(p => p.Tag is int);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent == null) yield break;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    // Close button
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // 3-dots filter menu button handlers
    private void QuestPlannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag && tag == "ContextMenu")
        {
            if (btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }
    }

    // Syncs ContextMenu IsChecked state from config each time the menu opens.
    // Required because ContextMenu is outside the visual tree — bindings don't work reliably.
    private void FilterMenu_Opened(object sender, RoutedEventArgs e)
    {
        MnuKappaFilter.IsChecked = _vm.KappaFilterEnabled;
    }

    // Handles filter MenuItem click: reads Tag to identify which filter, reads IsChecked, updates VM.
    private void FilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string tag)
        {
            switch (tag)
            {
                case "KappaFilter":
                    _vm.KappaFilterEnabled = item.IsChecked;
                    break;
            }
        }
    }

    // Handles All Maps section header row click — toggles expand/collapse.
    private void AllMapsRow_Click(object sender, MouseButtonEventArgs e)
    {
        _allMapsExpanded = !_allMapsExpanded;
        AllMapsContentPanel.Visibility = _allMapsExpanded ? Visibility.Visible : Visibility.Collapsed;
        AllMapsChevron.Text = _allMapsExpanded ? "▼" : "▶";
    }

    // Drag handle
    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BringToFrontRequested?.Invoke(this, EventArgs.Empty);

        DragHandle.CaptureMouse();
        _dragStart = e.GetPosition(this);
        DragHandle.MouseMove += DragHandle_MouseMove;
        DragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            var delta = pos - _dragStart;
            DragRequested?.Invoke(this, new PanelDragEventArgs(delta.X, delta.Y));
        }
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DragHandle.ReleaseMouseCapture();
        DragHandle.MouseMove -= DragHandle_MouseMove;
        DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
    }

    // Resize handle
    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        _dragStart = e.GetPosition(this);

        ((UIElement)sender).MouseMove += ResizeHandle_MouseMove;
        ((UIElement)sender).MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            var delta = pos - _dragStart;
            ResizeRequested?.Invoke(this, new PanelResizeEventArgs(delta.X, delta.Y));
            _dragStart = pos;
        }
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).MouseMove -= ResizeHandle_MouseMove;
        ((UIElement)sender).MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;
        ((UIElement)sender).ReleaseMouseCapture();
    }
}
