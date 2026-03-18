#nullable enable
using System.ComponentModel;
using System.Windows.Threading;
using eft_dma_radar.Tarkov.QuestPlanner;
using eft_dma_radar.Tarkov.QuestPlanner.Models;
using eft_dma_radar.UI.Misc;

namespace eft_dma_radar.UI.Pages;

/// <summary>
/// Reactive ViewModel for the Quest Planner panel.
/// Provides data binding for quest summary and connection state.
/// Uses a 1-second DispatcherTimer to poll QuestPlannerWorker.Current.
/// </summary>
public sealed class QuestPlannerViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly DispatcherTimer _refreshTimer;

    // --- Backing fields ---
    private QuestSummary? _currentSummary;
    private QuestConnectionState _connectionState = QuestConnectionState.Disconnected;
    private DateTime? _lastReadTime;
    private bool _isStale;

    public QuestPlannerViewModel()
    {
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnTimerTick;
    }

    // --- Public start/stop for panel lifecycle ---
    public void Start() => _refreshTimer.Start();
    public void Stop() => _refreshTimer.Stop();

    // --- Reactive properties ---
    public QuestSummary? CurrentSummary
    {
        get => _currentSummary;
        private set { _currentSummary = value; OnPropertyChanged(nameof(CurrentSummary)); }
    }

    public QuestConnectionState ConnectionState
    {
        get => _connectionState;
        private set { _connectionState = value; OnPropertyChanged(nameof(ConnectionState)); OnPropertyChanged(nameof(StatusText)); }
    }

    public DateTime? LastReadTime
    {
        get => _lastReadTime;
        private set { _lastReadTime = value; OnPropertyChanged(nameof(LastReadTime)); OnPropertyChanged(nameof(LastReadText)); }
    }

    public bool IsStale
    {
        get => _isStale;
        private set { _isStale = value; OnPropertyChanged(nameof(IsStale)); }
    }

    // --- Derived display properties ---
    public string StatusText => _connectionState switch
    {
        QuestConnectionState.Lobby => "Lobby",
        QuestConnectionState.InRaid => "In Raid",
        _ => "Disconnected"
    };

    public string LastReadText => _lastReadTime.HasValue
        ? _lastReadTime.Value.ToLocalTime().ToString("HH:mm:ss")
        : "--:--:--";

    // --- Empty state ---
    public bool ShowEmptyState => _currentSummary != null && _currentSummary.Maps.Count == 0 && _connectionState == QuestConnectionState.Lobby && !_isStale;

    public string EmptyStateMessage => _currentSummary?.TotalActiveQuests == 0
        ? "No active quests."
        : "No map objectives found for your active quests.";

    // --- Filter properties ---

    /// <summary>
    /// When true, map list shows only Kappa-required quests.
    /// Persisted to config; triggers ForceRecompute for immediate effect.
    /// </summary>
    public bool KappaFilterEnabled
    {
        get => ConfigManager.CurrentConfig.QuestPlanner.KappaFilter;
        set
        {
            if (ConfigManager.CurrentConfig.QuestPlanner.KappaFilter == value) return;
            ConfigManager.CurrentConfig.QuestPlanner.KappaFilter = value;
            ConfigManager.CurrentConfig.Save();
            OnPropertyChanged(nameof(KappaFilterEnabled));
            QuestPlannerWorker.ForceRecompute();
        }
    }

    /// <summary>
    /// True when there are active quests with no-map objectives to display in the All Maps section.
    /// </summary>
    public bool ShowAllMapsSection =>
        (_currentSummary?.AllMapsQuests?.Count ?? 0) > 0;

    /// <summary>
    /// Quests with no-map objectives for the All Maps section at the bottom of the panel.
    /// </summary>
    public IReadOnlyList<QuestPlan> AllMapsQuests =>
        _currentSummary?.AllMapsQuests ?? [];

    /// <summary>
    /// The top-scored (recommended) map, wrapped in a single-item list for the pinned sticky card.
    /// Empty when no maps are available.
    /// </summary>
    public IReadOnlyList<MapPlan> RecommendedMap =>
        _currentSummary?.Maps?.FirstOrDefault(m => m.IsRecommended) is { } map ? [map] : [];

    /// <summary>
    /// All maps except the recommended one, for the scrollable map list below the sticky card.
    /// </summary>
    public IReadOnlyList<MapPlan> OtherMaps =>
        _currentSummary?.Maps?.Where(m => !m.IsRecommended).ToList() as IReadOnlyList<MapPlan> ?? [];

    // --- Available Quest Banners ---

    /// <summary>
    /// True when there are quests available to start from traders.
    /// </summary>
    public bool ShowAvailableForStartBanner =>
        (_currentSummary?.AvailableForStartTraders?.Count ?? 0) > 0;

    /// <summary>
    /// Text for the AvailableForStart banner showing trader names.
    /// </summary>
    public string AvailableForStartText =>
        _currentSummary?.AvailableForStartTraders?.Count > 0
            ? $"Available tasks to start on traders: {string.Join(", ", _currentSummary.AvailableForStartTraders)}"
            : string.Empty;

    /// <summary>
    /// True when there are quests ready to turn in to traders.
    /// </summary>
    public bool ShowAvailableForFinishBanner =>
        (_currentSummary?.AvailableForFinishTraders?.Count ?? 0) > 0;

    /// <summary>
    /// Text for the AvailableForFinish banner showing trader names.
    /// </summary>
    public string AvailableForFinishText =>
        _currentSummary?.AvailableForFinishTraders?.Count > 0
            ? $"Available tasks to finish on traders: {string.Join(", ", _currentSummary.AvailableForFinishTraders)}"
            : string.Empty;

    // --- Hand Over Items Banner ---

    /// <summary>
    /// True when there are Started quests where only giveQuestItem objectives remain.
    /// These are shown in the "Hand over items" banner — player has the item but hasn't traded it in.
    /// </summary>
    public bool ShowHandOverItemsBanner =>
        (_currentSummary?.HandOverItems?.Count ?? 0) > 0;

    /// <summary>
    /// Multi-line text for the "Hand over items" banner.
    /// Each line: "{QuestName} — Hand over {ItemShortName}"
    /// </summary>
    public string HandOverItemsText =>
        _currentSummary?.HandOverItems?.Count > 0
            ? string.Join("\n", _currentSummary.HandOverItems.Select(i => i.DisplayText))
            : string.Empty;

    // --- Find in Raid Category ---

    /// <summary>
    /// True when there are find-in-raid item pairs to show in the All Maps FIR category.
    /// </summary>
    public bool ShowFirCategory =>
        (_currentSummary?.FirItems?.Count ?? 0) > 0;

    /// <summary>
    /// Find-in-raid items for the "Find in raid" category at the top of All Maps.
    /// Each item has ProgressText, ItemShortName, QuestName for display.
    /// </summary>
    public IReadOnlyList<FirItemInfo> FirItems =>
        _currentSummary?.FirItems ?? [];

    // --- Timer tick ---
    private void OnTimerTick(object? sender, EventArgs e)
    {
        var previousState = _connectionState;
        var state = QuestPlannerWorker.State;
        var summary = QuestPlannerWorker.Current;
        var isStale = QuestPlannerWorker.IsStale;

        ConnectionState = state;
        IsStale = isStale;

        // Notify when state changes
        if (previousState != state)
        {
            OnPropertyChanged(nameof(ShowEmptyState));
        }

        if (summary != null && summary != _currentSummary)
        {
            CurrentSummary = summary;
            LastReadTime = summary.ComputedAt;
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(EmptyStateMessage));
            OnPropertyChanged(nameof(ShowAllMapsSection));
            OnPropertyChanged(nameof(AllMapsQuests));
            OnPropertyChanged(nameof(RecommendedMap));
            OnPropertyChanged(nameof(OtherMaps));
            OnPropertyChanged(nameof(ShowAvailableForStartBanner));
            OnPropertyChanged(nameof(AvailableForStartText));
            OnPropertyChanged(nameof(ShowAvailableForFinishBanner));
            OnPropertyChanged(nameof(AvailableForFinishText));
            OnPropertyChanged(nameof(ShowHandOverItemsBanner));
            OnPropertyChanged(nameof(HandOverItemsText));
            OnPropertyChanged(nameof(ShowFirCategory));
            OnPropertyChanged(nameof(FirItems));
        }
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
