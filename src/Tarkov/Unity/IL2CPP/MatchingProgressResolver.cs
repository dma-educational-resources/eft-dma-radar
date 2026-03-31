using System;
using System.Diagnostics;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Resolves the live <c>MatchingProgress</c> instance.
    ///
    /// Strategy: <c>GameObjectManager.FindBehaviourByClassName("MatchingProgressView")</c>
    /// returns the <c>objectClass</c> ptr of the first matching component, identical to the
    /// pattern used by <c>AntiAfk</c> for <c>TarkovApplication</c>.
    /// From there: <c>objectClass + Offsets.MatchingProgressView._matchingProgress</c>
    /// → <c>MatchingProgress</c> instance pointer.
    ///
    /// <c>MatchingProgressView</c> is a pre-raid matchmaking UI MonoBehaviour — it only
    /// exists in the GOM while the queue / matching screen is active.
    /// </summary>
    internal static class MatchingProgressResolver
    {
        private const string Tag = "[MatchingProgressResolver]";

        private static ulong _cachedMatchingProgress;
        private static ulong _cachedViewObjectClass;
        private static Enums.EMatchingStage _cachedStage;
        private static readonly object _lock = new();
        private static volatile int _resolvingAsync; // 0 = idle, 1 = running

        // ── Transition-tracking state ────────────────────────────────────────────
        private static Enums.EMatchingStage _prevStage = Enums.EMatchingStage.None;
        private static Enums.EMatchingStage _highWaterStage = Enums.EMatchingStage.None;
        private static readonly Stopwatch _totalSw = new();
        private static readonly Stopwatch _stageSw = new();

        // ── Background stage poller (runs independently of the main loop) ────────
        private static System.Threading.Timer _stagePoller;
        private static volatile bool _pollerActive;

        // ── View-disappearance detection ─────────────────────────────────────────
        private const int ViewGoneThreshold = 5;
        private static volatile int _consecutiveReadFailures;

        // ── GOM search skip (handles launched-mid-raid) ──────────────────────────
        private const int MaxGomFailures = 3;
        private static int _consecutiveGomFailures;

        // ── Tracks whether NotifyRaidStarted() already printed the session summary ──
        private static volatile bool _sessionSummaryLogged;

        // ── Set once GameWorld is confirmed; blocks TryUpdateStage / ResolveAsync ──
        private static volatile bool _raidStarted;

        // ─────────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once when a <c>LocalGameWorld</c> is found — the matching phase is over.
        /// Stops the stage poller and freezes the elapsed timer so the session-end
        /// summary reports accurate matching duration rather than in-raid time.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static void NotifyRaidStarted()
        {
            if (_raidStarted)
                return;

            _raidStarted = true;
            _totalSw.Stop();
            StopStagePoller();

            Enums.EMatchingStage highWater;
            double elapsed;
            lock (_lock)
            {
                highWater = _highWaterStage;
                elapsed = _totalSw.Elapsed.TotalSeconds;
            }

            if (highWater != Enums.EMatchingStage.None)
            {
                Log.WriteLine(
                    $"{Tag} ──── Matching session ended ────\n" +
                    $"{Tag}   Furthest stage reached : {highWater} ({(int)highWater}/17)\n" +
                    $"{Tag}   Total matching elapsed  : {elapsed:F1}s");
                _sessionSummaryLogged = true;
            }
        }

        /// <summary>
        /// Clear cached pointer (call on raid start / raid stop).
        /// </summary>
        public static void Reset()
        {
            // Snapshot state under lock before stopping the poller
            Enums.EMatchingStage highWater;
            double elapsed;
            bool wasRunning;
            lock (_lock)
            {
                highWater = _highWaterStage;
                elapsed = _totalSw.Elapsed.TotalSeconds;
                wasRunning = _totalSw.IsRunning;
            }

            // Log summary only if matching was aborted before NotifyRaidStarted() fired
            if (!_sessionSummaryLogged && (wasRunning || highWater != Enums.EMatchingStage.None))
            {
                Log.WriteLine(
                    $"{Tag} ──── Matching session ended (aborted) ────\n" +
                    $"{Tag}   Furthest stage reached : {highWater} ({(int)highWater}/17)\n" +
                    $"{Tag}   Total matching elapsed  : {elapsed:F1}s");
            }

            StopStagePoller();

            lock (_lock)
            {
                _cachedMatchingProgress = 0;
                _cachedViewObjectClass = 0;
                _cachedStage = Enums.EMatchingStage.None;
                _prevStage = Enums.EMatchingStage.None;
                _highWaterStage = Enums.EMatchingStage.None;
                _totalSw.Reset();
                _stageSw.Reset();
            }
            Interlocked.Exchange(ref _consecutiveReadFailures, 0);
            _consecutiveGomFailures = 0;
            _resolvingAsync = 0;
            _sessionSummaryLogged = false;
            _raidStarted = false;
            Log.Write(AppLogLevel.Debug, "Cache invalidated.", "MatchingProgressResolver");
        }

        /// <summary>
        /// Non-blocking cache read.
        /// Returns <c>true</c> (and a non-zero <paramref name="matchingProgress"/>) if a
        /// valid pointer is cached.
        /// </summary>
        public static bool TryGetCached(out ulong matchingProgress)
        {
            lock (_lock)
            {
                matchingProgress = _cachedMatchingProgress;
                return matchingProgress.IsValidVirtualAddress();
            }
        }

        /// <summary>
        /// Fire-and-forget background resolve.
        /// Safe to call from any thread; does not block caller.
        /// </summary>
        public static void ResolveAsync()
        {
            if (_raidStarted)
                return;

            if (Interlocked.CompareExchange(ref _resolvingAsync, 1, 0) != 0)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var mp = GetMatchingProgress();
                    if (mp.IsValidVirtualAddress())
                        Log.Write(AppLogLevel.Debug, $"ResolveAsync: MatchingProgress @ 0x{mp:X}", "MatchingProgressResolver");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"{Tag} ResolveAsync error: {ex}");
                }
                finally
                {
                    _resolvingAsync = 0;
                }
            });
        }

        private static void HandleGomFailure()
        {
            _consecutiveGomFailures++;
            if (_consecutiveGomFailures == MaxGomFailures)
                Log.WriteLine($"{Tag} MatchingProgressView not found in GOM after {_consecutiveGomFailures} attempts.");
        }

        /// <summary>
        /// Synchronous resolver. Returns the cached value on subsequent calls.
        /// Walks the GOM by class name — same pattern as <c>AntiAfk.TarkovApplication</c>.
        /// </summary>
        public static ulong GetMatchingProgress()
        {
            if (TryGetCached(out var cached))
                return cached;

            try
            {
                var gomAddr = Memory.GOM;
                if (!gomAddr.IsValidVirtualAddress())
                    return 0;

                var gom = GameObjectManager.Get(gomAddr);

                // FindBehaviourByClassName returns the objectClass ptr of the first
                // component whose IL2CPP class name matches — exactly like AntiAfk does
                // for "TarkovApplication".
                ulong viewObjectClass;
                try
                {
                    viewObjectClass = gom.FindBehaviourByClassName("MatchingProgressView");
                }
                catch
                {
                    // Memory unreadable during GOM scan — treat same as "not found"
                    HandleGomFailure();
                    return 0;
                }

                if (!viewObjectClass.IsValidVirtualAddress())
                {
                    HandleGomFailure();
                    return 0;
                }

                _consecutiveGomFailures = 0; // successful find — reset counter

                Log.Write(AppLogLevel.Debug, $"MatchingProgressView objectClass @ 0x{viewObjectClass:X}", "MatchingProgressResolver");

                var mpPtr = Memory.ReadPtr(viewObjectClass + Offsets.MatchingProgressView._matchingProgress);
                if (!mpPtr.IsValidVirtualAddress())
                {
                    Log.Write(AppLogLevel.Debug, $"_matchingProgress ptr invalid @ objectClass+0x{Offsets.MatchingProgressView._matchingProgress:X}", "MatchingProgressResolver");
                    return 0;
                }

                lock (_lock)
                {
                    _cachedViewObjectClass = viewObjectClass;
                    _cachedMatchingProgress = mpPtr;
                }

                Log.Write(AppLogLevel.Info, $"MatchingProgress resolved @ 0x{mpPtr:X}", "MatchingProgressResolver");
                _totalSw.Restart();
                _stageSw.Restart();
                TryUpdateStage();
                StartStagePoller();
                return mpPtr;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"{Tag} GetMatchingProgress error: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// Reads the live <c>CurrentStage</c> from the cached <c>MatchingProgress</c> pointer,
        /// updates <see cref="_cachedStage"/>, and logs it. Call on every pre-raid loop tick.
        /// Returns <c>true</c> when the pointer is valid and the read succeeded.
        /// </summary>
        public static bool TryUpdateStage()
        {
            if (_raidStarted)
                return false;

            ulong mp;
            lock (_lock)
                mp = _cachedMatchingProgress;

            if (!mp.IsValidVirtualAddress())
                return false;

            try
            {
                // Memory read outside the lock — this is the slow path.
                var stage = (Enums.EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStage, useCache: false);

                // All state mutation under a single lock acquisition to prevent the
                // poller thread and the main memory thread from double-logging transitions.
                bool didTransition;
                Enums.EMatchingStage prevForLog;
                double stageElapsed, totalElapsed;
                bool needsSnapshot;

                lock (_lock)
                {
                    _cachedStage = stage;
                    Interlocked.Exchange(ref _consecutiveReadFailures, 0);

                    if (stage != _prevStage)
                    {
                        prevForLog = _prevStage;
                        stageElapsed = _stageSw.Elapsed.TotalSeconds;
                        totalElapsed = _totalSw.Elapsed.TotalSeconds;
                        needsSnapshot = stage >= Enums.EMatchingStage.LocalGameStarting;
                        didTransition = true;

                        if ((int)stage > (int)_highWaterStage)
                            _highWaterStage = stage;

                        _prevStage = stage;
                        _stageSw.Restart();
                    }
                    else
                    {
                        didTransition = false;
                        prevForLog = default;
                        stageElapsed = totalElapsed = 0;
                        needsSnapshot = false;
                    }
                }

                if (didTransition)
                {
                    Log.WriteLine(
                        $"{Tag} Stage TRANSITION: {prevForLog}({(int)prevForLog}) → {stage}({(int)stage}) | " +
                        $"prev held {stageElapsed:F1}s | total {totalElapsed:F1}s");

                    if (needsSnapshot)
                        LogSnapshot(mp);
                }

                return true;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _consecutiveReadFailures);
                Log.Write(AppLogLevel.Debug, $"TryUpdateStage read failure #{_consecutiveReadFailures}: {ex.Message}", "MatchingProgressResolver");
                return false;
            }
        }

        /// <summary>
        /// Returns the last successfully read <c>CurrentStage</c> without a memory read.
        /// Safe to call from the render thread.
        /// </summary>
        public static Enums.EMatchingStage GetCachedStage()
        {
            lock (_lock)
                return _cachedStage;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Background stage poller
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts a background <see cref="Timer"/> that calls <see cref="TryUpdateStage"/>
        /// every 100 ms, independent of the main game-instance loop. This ensures stage
        /// transitions are captured even while <c>GetLocalGameWorld</c> blocks.
        /// </summary>
        private static void StartStagePoller()
        {
            if (_pollerActive)
                return;

            _pollerActive = true;
            _stagePoller = new System.Threading.Timer(_ =>
            {
                try
                {
                    TryUpdateStage();

                    if (_consecutiveReadFailures >= ViewGoneThreshold)
                    {
                        Enums.EMatchingStage lastStage, highWater;
                        double totalElapsed;
                        int failures;

                        lock (_lock)
                        {
                            lastStage = _prevStage;
                            highWater = _highWaterStage;
                            totalElapsed = _totalSw.Elapsed.TotalSeconds;
                            failures = _consecutiveReadFailures;
                            // Clear the stale pointer so the next re-queue can re-resolve.
                            _cachedMatchingProgress = 0;
                            _cachedViewObjectClass = 0;
                        }

                        Log.WriteLine(
                            $"{Tag} ██ MatchingProgressView DISAPPEARED from GOM ██\n" +
                            $"{Tag}   Last known stage     : {lastStage} ({(int)lastStage}/17)\n" +
                            $"{Tag}   Furthest stage       : {highWater} ({(int)highWater}/17)\n" +
                            $"{Tag}   Total elapsed        : {totalElapsed:F1}s\n" +
                            $"{Tag}   Consecutive failures : {failures}");
                        StopStagePoller();
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"{Tag} StagePoller tick error: {ex.Message}");
                }
            }, null, 0, 100);

            Log.Write(AppLogLevel.Debug, "Stage poller started.", "MatchingProgressResolver");
        }

        /// <summary>
        /// Stops and disposes the background stage poller.
        /// </summary>
        private static void StopStagePoller()
        {
            _pollerActive = false;
            var t = Interlocked.Exchange(ref _stagePoller, null);
            if (t != null)
            {
                t.Dispose();
                Log.Write(AppLogLevel.Debug, "Stage poller stopped.", "MatchingProgressResolver");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Diagnostic snapshots
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the <c>MatchingProgressView</c> component-level fields and writes them to
        /// <c>XMLogging</c>. Uses the cached <c>viewObjectClass</c> when <paramref name="view"/> is 0.
        /// </summary>
        public static void LogViewSnapshot(ulong view = 0)
        {
            if (!Log.EnableDebugLogging)
                return;

            if (view == 0)
            {
                lock (_lock)
                    view = _cachedViewObjectClass;
            }

            if (!view.IsValidVirtualAddress())
                return;

            try
            {
                var serversLimited = Memory.ReadValue<bool>(view + Offsets.MatchingProgressView._serversLimited, useCache: false);
                var canUpdateStatus = Memory.ReadValue<bool>(view + Offsets.MatchingProgressView._canUpdateStatus, useCache: false);
                var maxMatchingTime = Memory.ReadValue<int>(view + Offsets.MatchingProgressView._maxMatchingTimeInSeconds, useCache: false);
                var warningHasValue = Memory.ReadValue<bool>(view + Offsets.MatchingProgressView._matchingWarningType_hasValue, useCache: false);
                var warningRaw = warningHasValue
                    ? Memory.ReadValue<int>(view + Offsets.MatchingProgressView._matchingWarningType, useCache: false)
                    : (int?)null;

                Log.Write(AppLogLevel.Debug,
                    $"ViewSnapshot @ 0x{view:X} | " +
                    $"ServersLimited={serversLimited} CanUpdateStatus={canUpdateStatus} " +
                    $"MaxMatchingTime={maxMatchingTime}s " +
                    $"MatchingWarning={(warningRaw.HasValue ? warningRaw.Value.ToString() : "null")}",
                    "MatchingProgressResolver");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"LogViewSnapshot error: {ex}", "MatchingProgressResolver");
            }
        }

        /// <summary>
        /// Reads all known <c>MatchingProgress</c> fields from <paramref name="mp"/> and
        /// writes a snapshot to <c>XMLogging</c>. Uses the cached pointer when
        /// <paramref name="mp"/> is 0.
        /// </summary>
        public static void LogSnapshot(ulong mp = 0)
        {
            if (!Log.EnableDebugLogging)
                return;

            if (mp == 0)
            {
                lock (_lock)
                    mp = _cachedMatchingProgress;
            }

            if (!mp.IsValidVirtualAddress())
                return;

            try
            {
                var currentStage = (Enums.EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStage, useCache: false);
                var currentStageGroup = (Enums.EMatchingStageGroup)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStageGroup, useCache: false);
                var stageProgress = Memory.ReadValue<float>(mp + Offsets.MatchingProgress.CurrentStageProgress, useCache: false);
                var estimateTime = Memory.ReadValue<int>(mp + Offsets.MatchingProgress.EstimateTime, useCache: false);
                var isAbortAvailable = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.IsAbortAvailable, useCache: false);
                var blockAbortDuration = Memory.ReadValue<int>(mp + Offsets.MatchingProgress.BlockAbortAbilityDurationSeconds, useCache: false);
                var showAbortPopup = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.ShowAbortConfirmationPopup, useCache: false);
                var abortRequested = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.IsMatchingAbortRequested, useCache: false);
                var canProcessStages = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.CanProcessServerStages, useCache: false);
                var lastDelayedStage = (Enums.EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.LastMemorizedDelayedStage, useCache: false);
                var lastDelayedProgress = Memory.ReadValue<float>(mp + Offsets.MatchingProgress.LastMemorizedDelayedStageProgress, useCache: false);

                Log.Write(AppLogLevel.Debug,
                    $"Snapshot @ 0x{mp:X} | " +
                    $"Stage={currentStage}({(int)currentStage}) Group={currentStageGroup}({(int)currentStageGroup}) " +
                    $"Progress={stageProgress:F3} EstimateTime={estimateTime}s | " +
                    $"LastDelayedStage={lastDelayedStage}({(int)lastDelayedStage}) LastDelayedProgress={lastDelayedProgress:F3} | " +
                    $"IsAbortAvailable={isAbortAvailable} BlockAbortDuration={blockAbortDuration}s " +
                    $"ShowAbortPopup={showAbortPopup} AbortRequested={abortRequested} " +
                    $"CanProcessStages={canProcessStages}",
                    "MatchingProgressResolver");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"LogSnapshot error: {ex}", "MatchingProgressResolver");
            }
        }
    }
}
