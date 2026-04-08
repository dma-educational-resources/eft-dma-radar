#nullable enable
using eft_dma_radar.Tarkov.Hideout;
using eft_dma_radar.UI.Misc;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Pages;

/// <summary>
/// Row item bound to the stash DataGrid.
/// </summary>
public sealed class StashItemView
{
    public string Name { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public int StackCount { get; init; }
    public string TraderFmt { get; init; } = string.Empty;
    public string TraderName { get; init; } = string.Empty;
    public string FleaFmt { get; init; } = string.Empty;
    public string BestFmt { get; init; } = string.Empty;
    public string SellOn { get; init; } = string.Empty;
    public bool SellOnFlea { get; init; }
    public long TraderRaw { get; init; }
    public long FleaRaw { get; init; }
    public long BestRaw { get; init; }
}

/// <summary>
/// Row item bound to the upgrades area list.
/// </summary>
public sealed class AreaUpgradeView
{
    private static readonly System.Windows.Media.Brush _green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly System.Windows.Media.Brush _orange = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00));
    private static readonly System.Windows.Media.Brush _grey = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly System.Windows.Media.Brush _slate = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x78, 0x90, 0x9C));

    public string AreaName { get; }
    public string LevelLabel { get; }
    public string StatusLabel { get; }
    public System.Windows.Media.Brush StatusBrush { get; }
    public double RowOpacity { get; }
    public Visibility RequirementsVisibility { get; }
    public IReadOnlyList<RequirementView> Requirements { get; }

    public AreaUpgradeView(HideoutAreaInfo info)
    {
        AreaName = FormatName(info.AreaType.ToString());
        RequirementsVisibility = info.IsMaxLevel ? Visibility.Collapsed : Visibility.Visible;
        RowOpacity = info.IsMaxLevel ? 0.38 : 1.0;
        StatusLabel = FormatStatus(info.Status);
        StatusBrush = GetBrush(info.Status);
        LevelLabel = info.IsMaxLevel
            ? $"lv{info.CurrentLevel}"
            : $"lv{info.CurrentLevel} → {info.CurrentLevel + 1}";
        Requirements = info.NextLevelRequirements
            .Select(r => new RequirementView(r))
            .ToList();
    }

    private static string FormatName(string raw) =>
        Regex.Replace(raw, "([a-z])([A-Z])", "$1 $2");

    private static string FormatStatus(EAreaStatus s) => s switch
    {
        EAreaStatus.ReadyToConstruct => "BUILD",
        EAreaStatus.ReadyToUpgrade => "UPGRADE",
        EAreaStatus.ReadyToInstallConstruct or
        EAreaStatus.ReadyToInstallUpgrade => "INSTALL",
        EAreaStatus.Constructing => "Building…",
        EAreaStatus.Upgrading => "Upgrading…",
        EAreaStatus.AutoUpgrading => "Auto",
        EAreaStatus.LockedToConstruct or EAreaStatus.LockedToUpgrade => "Locked",
        EAreaStatus.NoFutureUpgrades => "Max",
        _ => s.ToString()
    };

    private static System.Windows.Media.Brush GetBrush(EAreaStatus s) => s switch
    {
        EAreaStatus.ReadyToConstruct or EAreaStatus.ReadyToUpgrade or
        EAreaStatus.ReadyToInstallConstruct or EAreaStatus.ReadyToInstallUpgrade => _green,
        EAreaStatus.Constructing or EAreaStatus.Upgrading or
        EAreaStatus.AutoUpgrading => _orange,
        EAreaStatus.NoFutureUpgrades => _slate,
        _ => _grey
    };
}

/// <summary>
/// Single requirement row inside an area upgrade card.
/// </summary>
public sealed class RequirementView
{
    private static readonly System.Windows.Media.Brush _green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly System.Windows.Media.Brush _red = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x53, 0x50));

    public string Description { get; }
    public System.Windows.Media.Brush FulfilledBrush { get; }
    public string FulfilledIcon { get; }

    public RequirementView(HideoutRequirement req)
    {
        FulfilledBrush = req.Fulfilled ? _green : _red;
        FulfilledIcon = req.Fulfilled ? "✓" : "✗";
        Description = req.Type switch
        {
            ERequirementType.Item or ERequirementType.Tool when !req.Fulfilled
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}  ({req.CurrentCount}/{req.RequiredCount})",
            ERequirementType.Item or ERequirementType.Tool
                => $"{req.ItemName ?? req.ItemTemplateId ?? "?"} ×{req.RequiredCount}",
            ERequirementType.Area
                => $"{Regex.Replace(req.RequiredArea.ToString(), "([a-z])([A-Z])", "$1 $2")} lv{req.RequiredLevel}",
            ERequirementType.Skill
                => $"{req.SkillName ?? "Skill"} lv{req.SkillLevel}",
            ERequirementType.TraderLoyalty
                => $"{req.TraderName ?? req.TraderId ?? "Trader"} LL{req.LoyaltyLevel}",
            ERequirementType.TraderUnlock => "Trader unlock",
            ERequirementType.QuestComplete => "Quest complete",
            _ => req.Type.ToString()
        };
    }
}

/// <summary>
/// Floating panel — Stash value summary and hideout upgrade requirements.
/// </summary>
public partial class HideoutStashControl : UserControl
{
    private static Config Config => Program.Config;

    public event EventHandler? CloseRequested;
    public event EventHandler? BringToFrontRequested;
    public event EventHandler<PanelDragEventArgs>? DragRequested;
    public event EventHandler<PanelResizeEventArgs>? ResizeRequested;

    private readonly ObservableCollection<StashItemView> _items = new();
    private readonly ObservableCollection<StashItemView> _groupedItems = new();
    private readonly ObservableCollection<AreaUpgradeView> _areas = new();
    private readonly ICollectionView _view;
    private bool _isGrouped;
    private Point _dragStart;

    public HideoutStashControl()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        StashGrid.ItemsSource = _view;
        UpgradesList.ItemsSource = _areas;
        ChkMarkInRaid.IsChecked = Config.LootHideoutRequired;
        IsVisibleChanged += OnVisibleChanged;
    }

    // Populate on first show if a previous refresh already loaded data
    private bool _initialPopulateDone;
    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && !_initialPopulateDone &&
            (Program.Hideout.Items.Count > 0 || Program.Hideout.Areas.Count > 0))
        {
            _initialPopulateDone = true;
            PopulateGrid(Program.Hideout);
            PopulateUpgrades(Program.Hideout);
            TxtItemCount.Text = $"{Program.Hideout.Items.Count} items";
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
            var first = g.First();
            var totalQty = g.Sum(i => i.StackCount);
            var traderRaw = first.TraderRaw / Math.Max(1, first.StackCount) * totalQty;
            var fleaRaw = first.FleaRaw / Math.Max(1, first.StackCount) * totalQty;
            var bestRaw = Math.Max(traderRaw, fleaRaw);
            var sellOnFlea = fleaRaw > traderRaw;
            _groupedItems.Add(new StashItemView
            {
                Name = first.Name,
                Id = first.Id,
                StackCount = totalQty,
                TraderFmt = FormatPrice(traderRaw),
                TraderName = first.TraderName,
                FleaFmt = FormatPrice(fleaRaw),
                BestFmt = FormatPrice(bestRaw),
                SellOn = sellOnFlea ? "Flea" : "Trader",
                SellOnFlea = sellOnFlea,
                TraderRaw = traderRaw,
                FleaRaw = fleaRaw,
                BestRaw = bestRaw,
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

    private void ChkMarkInRaid_Changed(object sender, RoutedEventArgs e)
    {
        Config.LootHideoutRequired = ChkMarkInRaid.IsChecked == true;
        ConfigManager.CurrentConfig.Save();
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
                PopulateUpgrades(Program.Hideout);
                TxtItemCount.Text = status;
            });
        }
        finally
        {
            await Dispatcher.InvokeAsync(() => BtnRefresh.IsEnabled = true);
        }
    }

    // ── Stash ─────────────────────────────────────────────────────────────

    private void PopulateGrid(HideoutManager hideout)
    {
        _items.Clear();
        foreach (var si in hideout.Items)
        {
            _items.Add(new StashItemView
            {
                Name = si.Name,
                Id = si.Id,
                StackCount = si.StackCount,
                TraderFmt = FormatPrice(si.TraderPrice * si.StackCount),
                TraderName = si.BestTraderName,
                FleaFmt = FormatPrice(si.FleaPrice * si.StackCount),
                BestFmt = FormatPrice(si.BestPrice),
                SellOn = si.SellOnFlea ? "Flea" : "Trader",
                SellOnFlea = si.SellOnFlea,
                TraderRaw = si.TraderPrice * si.StackCount,
                FleaRaw = si.FleaPrice * si.StackCount,
                BestRaw = si.BestPrice,
            });
        }

        TxtTraderTotal.Text = FormatPrice(hideout.TotalTraderValue);
        TxtFleaTotal.Text = FormatPrice(hideout.TotalFleaValue);
        TxtBestTotal.Text = FormatPrice(hideout.TotalBestValue);
        if (_isGrouped)
            RebuildGrouped();
        _view.Refresh();
    }

    // ── Upgrades ──────────────────────────────────────────────────────────

    private void PopulateUpgrades(HideoutManager hideout)
    {
        _areas.Clear();

        if (hideout.Areas.Count == 0)
        {
            TxtUpgradeCount.Text = "No area data — press Refresh while in the hideout";
            return;
        }

        foreach (var area in hideout.Areas
            .OrderBy(a => a.IsMaxLevel ? 1 : 0)
            .ThenBy(a => GetStatusPriority(a.Status))
            .ThenBy(a => (int)a.AreaType))
        {
            _areas.Add(new AreaUpgradeView(area));
        }

        var ready = hideout.Areas.Count(a =>
            a.Status is EAreaStatus.ReadyToUpgrade or EAreaStatus.ReadyToConstruct);
        var upgradeable = hideout.Areas.Count(a => !a.IsMaxLevel);
        var maxed = hideout.Areas.Count - upgradeable;

        TxtUpgradeCount.Text =
            $"{ready} ready  ·  {upgradeable} upgradeable  ·  {maxed} maxed";
    }

    private static int GetStatusPriority(EAreaStatus s) => s switch
    {
        EAreaStatus.ReadyToUpgrade or EAreaStatus.ReadyToConstruct => 0,
        EAreaStatus.ReadyToInstallUpgrade or EAreaStatus.ReadyToInstallConstruct => 1,
        EAreaStatus.Upgrading or EAreaStatus.Constructing => 2,
        EAreaStatus.AutoUpgrading => 3,
        EAreaStatus.LockedToUpgrade or EAreaStatus.LockedToConstruct => 4,
        _ => 5
    };

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FormatPrice(long price) => price switch
    {
        >= 1_000_000 => $"{price / 1_000_000.0:0.##}M ₽",
        >= 1_000 => $"{price / 1_000.0:0}K ₽",
        _ => $"{price} ₽",
    };

    // ── Close ────────────────────────────────────────────────────────────

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    // ── Drag ─────────────────────────────────────────────────────────────

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T match) return match;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src &&
            FindParent<System.Windows.Controls.Button>(src) is not null)
            return;

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
            var delta = e.GetPosition(this) - _dragStart;
            DragRequested?.Invoke(this, new PanelDragEventArgs(delta.X, delta.Y));
        }
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DragHandle.ReleaseMouseCapture();
        DragHandle.MouseMove -= DragHandle_MouseMove;
        DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
    }

    // ── Resize ───────────────────────────────────────────────────────────

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
