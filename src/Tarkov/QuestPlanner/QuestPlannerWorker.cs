using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.QuestPlanner.Models;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;
using SDK;
using static eft_dma_radar.Tarkov.MemoryInterface;

namespace eft_dma_radar.Tarkov.QuestPlanner;

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
/// Background orchestrator that pipelines QuestMemoryReader and QuestPlanBuilder on a ~10s lobby poll.
/// Implements change detection to skip recomputation when quest state is unchanged.
/// Exposes Current (latest QuestSummary) and State (Lobby/InRaid/Disconnected)
/// as static properties for the Quest Planner UI tab.
/// </summary>
internal static class QuestPlannerWorker
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
    /// The latest computed quest summary, or null when not in lobby.
    /// Read by the Quest Planner UI tab.
    /// </summary>
    public static QuestSummary? Current { get; private set; }

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
    private static volatile bool _forceRecompute = true;

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
            Name = "QuestPlannerWorker"
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
        XMLogging.WriteLine("[QuestPlannerWorker] Thread starting...");

        while (true)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException is { } inner ? $"{ex.Message} | Inner: {inner.Message}" : ex.Message;
                XMLogging.WriteLine($"[QuestPlannerWorker] ERROR: {msg}");
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
                XMLogging.WriteLine("[QuestPlannerWorker] DMA disconnected");
                State = QuestConnectionState.Disconnected;
                Current = null;
                IsStale = false;
                _forceRecompute = true;
                _stateTransitionTime = DateTime.UtcNow;
            }
            return;
        }

        // 2. Check if in raid - skip quest planning during raids
        if (Memory.InRaid)
        {
            if (State != QuestConnectionState.InRaid)
            {
                XMLogging.WriteLine("[QuestPlannerWorker] In raid - suspending quest planning");
                State = QuestConnectionState.InRaid;
                Current = null;
                IsStale = false;
                _forceRecompute = true; // Force recompute when we return to lobby
                _stateTransitionTime = DateTime.UtcNow;
            }
            return;
        }

        // 3. We are in lobby (connected, not in raid) - do lobby poll
        State = QuestConnectionState.Lobby;

        // 4. Resolve profile pointer
        var profileAddr = GetLobbyProfile();
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
                XMLogging.WriteLine("[QuestPlannerWorker] Profile not available - data may be stale");
                IsStale = Current != null; // Only stale if we had previous data
            }
            return;
        }

        // Profile resolved - clear stale flag
        IsStale = false;

        // 5. Ensure task data is ready before doing any memory reads or diff work
        if (!EftDataManager.IsInitialized)
        {
            XMLogging.WriteLine("[QuestPlannerWorker] TaskData not yet initialized - skipping");
            return;
        }

        // 6. Read quest state (all statuses: Started, AvailableForStart, AvailableForFinish)
        var quests = QuestMemoryReader.ReadAvailableQuests(profileAddr);

        // 7. Change detection: skip recompute if quest state unchanged
        if (!_forceRecompute && !HasQuestStateChanged(quests, _lastQuestState))
        {
            // No change - wait full lobby poll interval before next check (interruptible)
            _wakeSignal.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
            _wakeSignal.Reset();
            return;
        }

        // 8. Recompute quest summary
        XMLogging.WriteLine($"[QuestPlannerWorker] Recomputing plan ({quests.Started.Count} active quests)");

        var settings = ConfigManager.CurrentConfig.QuestPlanner;
        var summary = QuestPlanBuilder.GetSummary(quests, EftDataManager.TaskData, settings);
        Current = summary;
        _lastQuestState = quests;
        _forceRecompute = false;

        XMLogging.WriteLine($"[QuestPlannerWorker] Plan computed: {summary.Maps.Count} maps, {summary.TotalCompletableObjectives} objectives");

        // Wait remainder of lobby poll interval (interruptible)
        _wakeSignal.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
        _wakeSignal.Reset();
    }

    /// <summary>
    /// Resolves the player Profile pointer from TarkovApplication in the lobby.
    /// Chain: GOM.FindBehaviourByClassName("TarkovApplication") -> _menuOperation (0x130) -> _profile (0x50)
    /// Returns 0 on failure - never throws.
    /// </summary>
    private static ulong GetLobbyProfile()
    {
        try
        {
            var gom = GameObjectManager.Get(Memory.GOM);
            ulong app = gom.FindBehaviourByClassName("TarkovApplication");
            if (!app.IsValidVirtualAddress()) return 0;
            ulong menuOp = Memory.ReadPtr(app + Offsets.TarkovApplication._menuOperation);
            if (menuOp == 0) return 0;
            return Memory.ReadPtr(menuOp + Offsets.MainMenuShowOperation._profile);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Detects if quest state has changed by comparing quest IDs and completed conditions
    /// across all three status groups (Started, AvailableForStart, AvailableForFinish).
    /// </summary>
    private static bool HasQuestStateChanged(AvailableQuests current, AvailableQuests previous) =>
        HasQuestListChanged(current.Started, previous.Started)
        || HasQuestListChanged(current.AvailableForStart, previous.AvailableForStart)
        || HasQuestListChanged(current.AvailableForFinish, previous.AvailableForFinish);

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
        var prevById = previous.ToDictionary(q => q.Id, q => q.CompletedConditions, StringComparer.Ordinal);

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
