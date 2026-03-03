using System.Collections.Frozen;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.Tarkov.MissionPlanner.Models;
using static eft_dma_radar.Tarkov.MemoryInterface;

namespace eft_dma_radar.Tarkov.MissionPlanner;

/// <summary>
/// Connection state for quest planning purposes.
/// </summary>
public enum QuestConnectionState
{
    /// <summary>
    /// Game not running or DMA not connected.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Connected, in lobby (quest planning is valid).
    /// </summary>
    Lobby,

    /// <summary>
    /// Connected but inside a raid (planning skipped).
    /// </summary>
    InRaid
}

/// <summary>
/// Background orchestrator that pipelines ProfileAccessor, QuestReader, and MissionService
/// on a ~10s lobby poll. Implements change detection to skip recomputation when quest state
/// is unchanged. Exposes Current (latest MissionSummary) and State (Lobby/InRaid/Disconnected)
/// as static properties for UI consumption.
/// </summary>
internal static class QuestPlannerService
{
    /// <summary>
    /// Lobby poll interval (~10s matching eft-mission-reader behavior).
    /// </summary>
    private static readonly TimeSpan LobbyPollInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Signaled to wake the worker thread immediately (e.g., when filter settings change).
    /// </summary>
    private static readonly ManualResetEventSlim _wakeSignal = new(false);

    /// <summary>
    /// The latest computed mission summary, or null when not in lobby.
    /// Read by the UI tab and widget.
    /// </summary>
    public static MissionSummary? Current { get; private set; }

    /// <summary>
    /// Current connection state from the quest planner's perspective.
    /// </summary>
    public static QuestConnectionState State { get; private set; } = QuestConnectionState.Disconnected;

    /// <summary>
    /// True when in lobby but couldn't refresh data (profile not available).
    /// UI should show a warning banner when this is true.
    /// </summary>
    public static bool IsStale { get; private set; }

    /// <summary>
    /// Change detection: last-known snapshot of quest IDs + completed conditions.
    /// </summary>
    private static AvailableQuests _lastQuestState = new();

    /// <summary>
    /// Force recompute on first tick and after state transitions.
    /// </summary>
    private static bool _forceRecompute = true;

    /// <summary>
    /// Tracks when the last state transition occurred for grace period handling.
    /// </summary>
    private static DateTime _stateTransitionTime = DateTime.MinValue;

    /// <summary>
    /// Grace period after state transition before marking data as stale.
    /// Gives the game time to fully initialize after restart.
    /// </summary>
    private static readonly TimeSpan ProfileResolutionGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Initialize the background service. Called from Program.cs startup.
    /// </summary>
    internal static void ModuleInit()
    {
        new Thread(Worker)
        {
            IsBackground = true,
            Name = "QuestPlannerService"
        }.Start();
    }

    /// <summary>
    /// Forces a recompute on the next tick, regardless of change detection.
    /// Called by the UI when filter settings change to give immediate feedback.
    /// </summary>
    public static void ForceRecompute()
    {
        _forceRecompute = true;
        _wakeSignal.Set();
    }

    /// <summary>
    /// Background worker thread that polls for quest state changes.
    /// </summary>
    private static void Worker()
    {
        XMLogging.WriteLine("[QuestPlannerService] Thread starting...");

        while (true)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                var errorDetails = ex.Message;
                if (ex.InnerException != null)
                    errorDetails += $" | Inner: {ex.InnerException.Message}";
                XMLogging.WriteLine($"[QuestPlannerService] ERROR: {errorDetails}");
                State = QuestConnectionState.Disconnected;
                Current = null;
            }

            _wakeSignal.Wait(1000); // Base tick; Tick() adds extra delay for lobby polls
            _wakeSignal.Reset();
        }
    }

    /// <summary>
    /// Single tick of the poll loop. Handles state transitions and quest planning.
    /// </summary>
    private static void Tick()
    {
        // 1. Check DMA connection
        if (!Memory.Ready)
        {
            if (State != QuestConnectionState.Disconnected)
            {
                XMLogging.WriteLine("[QuestPlannerService] DMA disconnected");
                State = QuestConnectionState.Disconnected;
                Current = null;
                IsStale = false;
                _forceRecompute = true;
                _stateTransitionTime = DateTime.UtcNow;
                ProfileAccessor.ClearCache();
            }
            return;
        }

        // 2. Check if in raid - skip quest planning during raids
        if (Memory.InRaid)
        {
            if (State != QuestConnectionState.InRaid)
            {
                XMLogging.WriteLine("[QuestPlannerService] In raid - suspending quest planning");
                State = QuestConnectionState.InRaid;
                Current = null;
                IsStale = false;
                _forceRecompute = true; // Force recompute when we return to lobby
                _stateTransitionTime = DateTime.UtcNow;
                ProfileAccessor.ClearCache();
            }
            return;
        }

        // 3. We are in lobby (connected, not in raid) - do lobby poll
        State = QuestConnectionState.Lobby;

        // 4. Resolve profile pointer
        var profileAddr = ProfileAccessor.GetProfile();
        if (profileAddr == 0)
        {
            // Check if we're within grace period after state transition
            var timeSinceTransition = DateTime.UtcNow - _stateTransitionTime;
            if (timeSinceTransition < ProfileResolutionGracePeriod)
            {
                // Within grace period - don't mark as stale, just skip this tick
                // The profile pointer may not be available yet after game restart
                return;
            }

            // Grace period expired - now mark as stale
            if (!IsStale)
            {
                XMLogging.WriteLine("[QuestPlannerService] Profile not available - data may be stale");
                IsStale = Current != null; // Only stale if we had previous data
            }
            return;
        }

        // Profile resolved - clear stale flag
        IsStale = false;

        // 5. Read quest state (all statuses: Started, AvailableForStart, AvailableForFinish)
        var quests = QuestReader.ReadAvailableQuests(profileAddr);

        // 6. Change detection: skip recompute if quest state unchanged
        if (!_forceRecompute && !HasQuestStateChanged(quests, _lastQuestState))
        {
            // No change - wait full lobby poll interval before next check (interruptible)
            _wakeSignal.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
            _wakeSignal.Reset();
            return;
        }

        // 7. Recompute mission summary
        XMLogging.WriteLine($"[QuestPlannerService] Recomputing plan ({quests.Started.Count} active quests)");

        if (!EftDataManager.IsInitialized)
        {
            XMLogging.WriteLine("[QuestPlannerService] TaskData not yet initialized - skipping");
            return;
        }

        var settings = ConfigManager.CurrentConfig.QuestPlanner;
        var summary = MissionService.GetSummary(quests, EftDataManager.TaskData, NullStashFilter.Instance, settings);
        Current = summary;
        _lastQuestState = quests;
        _forceRecompute = false;

        XMLogging.WriteLine($"[QuestPlannerService] Plan computed: {summary.Maps.Count} maps, {summary.TotalCompletableObjectives} objectives");

        // Wait remainder of lobby poll interval (interruptible)
        _wakeSignal.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
        _wakeSignal.Reset();
    }

    /// <summary>
    /// Detects if quest state has changed by comparing quest IDs and completed conditions.
    /// Uses SetEquals for efficient comparison of completed condition sets.
    /// Compares all three status groups (Started, AvailableForStart, AvailableForFinish).
    /// </summary>
    private static bool HasQuestStateChanged(
        AvailableQuests current,
        AvailableQuests previous)
    {
        // Check Started quests
        if (HasQuestListChanged(current.Started, previous.Started))
            return true;

        // Check AvailableForStart quests
        if (HasQuestListChanged(current.AvailableForStart, previous.AvailableForStart))
            return true;

        // Check AvailableForFinish quests
        if (HasQuestListChanged(current.AvailableForFinish, previous.AvailableForFinish))
            return true;

        return false;
    }

    /// <summary>
    /// Compares two quest lists for changes in IDs or completed conditions.
    /// </summary>
    private static bool HasQuestListChanged(
        IReadOnlyList<QuestData> current,
        IReadOnlyList<QuestData> previous)
    {
        // Count mismatch -> changed
        if (current.Count != previous.Count) return true;

        // Build lookup from previous state
        var prevById = previous.ToDictionary(q => q.Id, q => q.CompletedConditions, StringComparer.OrdinalIgnoreCase);

        foreach (var quest in current)
        {
            if (!prevById.TryGetValue(quest.Id, out var prevCompleted))
                return true; // New quest appeared

            // SetEquals: same completed conditions?
            if (!quest.CompletedConditions.SetEquals(prevCompleted))
                return true;
        }

        return false;
    }
}
