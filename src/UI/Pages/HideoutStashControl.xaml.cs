#nullable enable
using eft_dma_radar.Tarkov.Hideout;
using eft_dma_radar.UI.Misc;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages;

/// <summary>
/// Row item bound to the stash DataGrid.
/// </summary>
public sealed class StashItemView
{
    public string Name       { get; init; } = string.Empty;
    public string Id         { get; init; } = string.Empty;
    public int    StackCount { get; init; }
    public string TraderFmt  { get; init; } = string.Empty;
    public string FleaFmt    { get; init; } = string.Empty;
    public string BestFmt    { get; init; } = string.Empty;
    public string SellOn     { get; init; } = string.Empty;
    public bool   SellOnFlea { get; init; }
    public long   TraderRaw  { get; init; }
    public long   FleaRaw    { get; init; }
    public long   BestRaw    { get; init; }
}

/// <summary>
/// Floating panel that displays every item in the hideout stash with
/// per-item trader / flea prices and a stash-total value summary.
/// </summary>
public partial class HideoutStashControl : UserControl
{
    public event EventHandler?                       CloseRequested;
    public event EventHandler?                       BringToFrontRequested;
    public event EventHandler<PanelDragEventArgs>?   DragRequested;
    public event EventHandler<PanelResizeEventArgs>? ResizeRequested;

    private readonly ObservableCollection<StashItemView> _items = new();
    private readonly ObservableCollection<StashItemView> _groupedItems = new();
    private readonly ICollectionView _view;
    private bool _isGrouped;
    private Point _dragStart;

    public HideoutStashControl()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        StashGrid.ItemsSource = _view;
        IsVisibleChanged += OnVisibleChanged;
    }

    // Populate on first show if startup scan already finished
    private bool _initialPopulateDone;
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && !_initialPopulateDone && Program.Hideout.Items.Count > 0)
        {
            _initialPopulateDone = true;
            PopulateGrid(Program.Hideout);
            TxtItemCount.Text = $"{Program.Hideout.Items.Count} items (startup scan)";
        }
    }

    // ── Group toggle ─────────────────────────────────────────────────────

    private void ChkGroup_Changed(object sender, RoutedEventArgs e)
    {
        _isGrouped = ChkGroup.IsChecked == true;
        RebuildGrouped();
        var source = _isGrouped ? _groupedItems : _items;
        var view = CollectionViewSource.GetDefaultView(source);
        view.Filter = FilterItem;
        StashGrid.ItemsSource = view;
    }

    private void RebuildGrouped()
    {
        _groupedItems.Clear();
        foreach (var g in _items
            .GroupBy(i => i.Id)
            .OrderBy(g => g.First().Name))
        {
            var first      = g.First();
            var totalQty   = g.Sum(i => i.StackCount);
            var traderRaw  = first.TraderRaw / Math.Max(1, first.StackCount) * totalQty;
            var fleaRaw    = first.FleaRaw   / Math.Max(1, first.StackCount) * totalQty;
            var bestRaw    = Math.Max(traderRaw, fleaRaw);
            var sellOnFlea = fleaRaw > traderRaw;
            _groupedItems.Add(new StashItemView
            {
                Name       = first.Name,
                Id         = first.Id,
                StackCount = totalQty,
                TraderFmt  = FormatPrice(traderRaw),
                FleaFmt    = FormatPrice(fleaRaw),
                BestFmt    = FormatPrice(bestRaw),
                SellOn     = sellOnFlea ? "Flea" : "Trader",
                SellOnFlea = sellOnFlea,
                TraderRaw  = traderRaw,
                FleaRaw    = fleaRaw,
                BestRaw    = bestRaw,
            });
        }
    }

    // ── Filtering ────────────────────────────────────────────────────────

    private bool FilterItem(object obj)
    {
        if (obj is not StashItemView item) return false;
        var search = SearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(search)) return true;
        return item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Id.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var activeView = CollectionViewSource.GetDefaultView(
            _isGrouped ? (System.Collections.IEnumerable)_groupedItems : _items);
        activeView.Refresh();
    }

    // ── Refresh ──────────────────────────────────────────────────────────

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        TxtItemCount.Text = "Refreshing...";
        try
        {
            var status = await Program.Hideout.RefreshAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                PopulateGrid(Program.Hideout);
                TxtItemCount.Text = status;
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => BtnRefresh.IsEnabled = true);
        }
    }

    private void PopulateGrid(HideoutManager hideout)
    {
        _items.Clear();
        foreach (var si in hideout.Items)
        {
            _items.Add(new StashItemView
            {
                Name       = si.Name,
                Id         = si.Id,
                StackCount = si.StackCount,
                TraderFmt  = FormatPrice(si.TraderPrice * si.StackCount),
                FleaFmt    = FormatPrice(si.FleaPrice   * si.StackCount),
                BestFmt    = FormatPrice(si.BestPrice),
                SellOn     = si.SellOnFlea ? "Flea" : "Trader",
                SellOnFlea = si.SellOnFlea,
                TraderRaw  = si.TraderPrice * si.StackCount,
                FleaRaw    = si.FleaPrice   * si.StackCount,
                BestRaw    = si.BestPrice,
            });
        }

        TxtTraderTotal.Text = FormatPrice(hideout.TotalTraderValue);
        TxtFleaTotal.Text   = FormatPrice(hideout.TotalFleaValue);
        TxtBestTotal.Text   = FormatPrice(hideout.TotalBestValue);
        // TxtItemCount is set by the caller (status message from RefreshAsync)
        if (_isGrouped)
            RebuildGrouped();
        _view.Refresh();
    }

    private static string FormatPrice(long price) => price switch
    {
        >= 1_000_000 => $"{price / 1_000_000.0:0.##}M ₽",
        >= 1_000     => $"{price / 1_000.0:0}K ₽",
        _            => $"{price} ₽",
    };

    // ── Close ────────────────────────────────────────────────────────────

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Drag ─────────────────────────────────────────────────────────────

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BringToFrontRequested?.Invoke(this, EventArgs.Empty);
        DragHandle.CaptureMouse();
        _dragStart = e.GetPosition(this);
        DragHandle.MouseMove          += DragHandle_MouseMove;
        DragHandle.MouseLeftButtonUp  += DragHandle_MouseLeftButtonUp;
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var delta = e.GetPosition(this) - _dragStart;
            DragRequested?.Invoke(this, new PanelDragEventArgs(delta.X, delta.Y));
        }
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DragHandle.ReleaseMouseCapture();
        DragHandle.MouseMove         -= DragHandle_MouseMove;
        DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
    }

    // ── Resize ───────────────────────────────────────────────────────────

    private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).CaptureMouse();
        _dragStart = e.GetPosition(this);
        ((UIElement)sender).MouseMove         += ResizeHandle_MouseMove;
        ((UIElement)sender).MouseLeftButtonUp += ResizeHandle_MouseLeftButtonUp;
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pos   = e.GetPosition(this);
            var delta = pos - _dragStart;
            ResizeRequested?.Invoke(this, new PanelResizeEventArgs(delta.X, delta.Y));
            _dragStart = pos;
        }
    }

    private void ResizeHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).MouseMove         -= ResizeHandle_MouseMove;
        ((UIElement)sender).MouseLeftButtonUp -= ResizeHandle_MouseLeftButtonUp;
        ((UIElement)sender).ReleaseMouseCapture();
    }
}
