using SilkUtils = eft_dma_radar.Silk.Misc.Utils;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Resolves the live <c>MatchingProgress</c> instance during the pre-raid matching screen.
    ///
    /// Strategy: GOM scan by klass pointer (primary) or class name (fallback) to find
    /// <c>MatchingProgressView</c>, then read its <c>_matchingProgress</c> field.
    /// A background timer polls <c>CurrentStage</c> every 100ms and logs transitions.
    /// </summary>
    internal static class MatchingProgressResolver
    {
        private const string Tag = "[MatchingProgressResolver]";

        private static ulong _cachedMatchingProgress;
        private static ulong _cachedViewObjectClass;
        private static EMatchingStage _cachedStage;
        private static readonly Lock _lock = new();
        private static volatile int _resolvingAsync; // 0 = idle, 1 = running

        // ── Transition-tracking state ────────────────────────────────────────
        private static EMatchingStage _prevStage = EMatchingStage.None;
        private static EMatchingStage _highWaterStage = EMatchingStage.None;
        private static readonly Stopwatch _totalSw = new();
        private static readonly Stopwatch _stageSw = new();

        // ── Background stage poller ──────────────────────────────────────────
        private static Timer? _stagePoller;
        private static volatile bool _pollerActive;

        // ── View-disappearance detection ─────────────────────────────────────
        private const int ViewGoneThreshold = 5;
        private static volatile int _consecutiveReadFailures;

        // ── GOM search skip (handles launched-mid-raid) ──────────────────────
        private const int MaxGomFailures = 3;
        private static int _consecutiveGomFailures;

        // ── Tracks whether NotifyRaidStarted() already printed the session summary ──
        private static volatile bool _sessionSummaryLogged;

        // ── Set once GameWorld is confirmed; blocks TryUpdateStage / ResolveAsync ──
        private static volatile bool _raidStarted;

        // ── Cached klass pointer for fast GOM scan ───────────────────────────
        private static ulong _cachedMatchingProgressViewKlass;

        // ─────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called once when a <c>LocalGameWorld</c> is found — the matching phase is over.
        /// Stops the stage poller and freezes the elapsed timer so the session-end
        /// summary reports accurate matching duration.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static void NotifyRaidStarted()
        {
            if (_raidStarted)
                return;

            _raidStarted = true;
            _totalSw.Stop();
            StopStagePoller();

            EMatchingStage highWater;
            double elapsed;
            lock (_lock)
            {
                highWater = _highWaterStage;
                elapsed = _totalSw.Elapsed.TotalSeconds;
            }

            if (highWater != EMatchingStage.None)
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
            EMatchingStage highWater;
            double elapsed;
            bool wasRunning;
            lock (_lock)
            {
                highWater = _highWaterStage;
                elapsed = _totalSw.Elapsed.TotalSeconds;
                wasRunning = _totalSw.IsRunning;
            }

            // Log summary only if matching was aborted before NotifyRaidStarted() fired
            if (!_sessionSummaryLogged && (wasRunning || highWater != EMatchingStage.None))
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
                _cachedStage = EMatchingStage.None;
                _prevStage = EMatchingStage.None;
                _highWaterStage = EMatchingStage.None;
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
                return SilkUtils.IsValidVirtualAddress(matchingProgress);
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
                    if (SilkUtils.IsValidVirtualAddress(mp))
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

        /// <summary>
        /// Reads the live <c>CurrentStage</c> from the cached <c>MatchingProgress</c> pointer,
        /// updates <see cref="_cachedStage"/>, and logs transitions.
        /// Returns <c>true</c> when the pointer is valid and the read succeeded.
        /// </summary>
        public static bool TryUpdateStage()
        {
            if (_raidStarted)
                return false;

            ulong mp;
            lock (_lock)
                mp = _cachedMatchingProgress;

            if (!SilkUtils.IsValidVirtualAddress(mp))
                return false;

            try
            {
                var stage = (EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStage, useCache: false);

                bool didTransition;
                EMatchingStage prevForLog;
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
                        needsSnapshot = stage >= EMatchingStage.LocalGameStarting;
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
        public static EMatchingStage GetCachedStage()
        {
            lock (_lock)
                return _cachedStage;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Synchronous resolver
        // ─────────────────────────────────────────────────────────────────────

        private static void HandleGomFailure()
        {
            _consecutiveGomFailures++;
            if (_consecutiveGomFailures == MaxGomFailures)
                Log.WriteLine($"{Tag} MatchingProgressView not found in GOM after {_consecutiveGomFailures} attempts.");
        }

        /// <summary>
        /// Synchronous resolver. Returns the cached value on subsequent calls.
        /// Primary: GOM scan by klass pointer (fast — avoids name string reads).
        /// Fallback: GOM scan by class name.
        /// </summary>
        public static ulong GetMatchingProgress()
        {
            if (TryGetCached(out var cached))
                return cached;

            try
            {
                var gomAddr = Memory.GOM;
                if (!SilkUtils.IsValidVirtualAddress(gomAddr))
                    return 0;

                var gom = GOM.Get(gomAddr);
                ulong viewObjectClass = 0;

                // Primary: klass-pointer-based GOM scan (saves ~2 DMA reads per component)
                try
                {
                    var klassPtr = _cachedMatchingProgressViewKlass;
                    if (!SilkUtils.IsValidVirtualAddress(klassPtr))
                    {
                        klassPtr = Il2CppDumper.ResolveKlassByTypeIndex(
                            Offsets.Special.MatchingProgressView_TypeIndex);
                        if (SilkUtils.IsValidVirtualAddress(klassPtr))
                        {
                            _cachedMatchingProgressViewKlass = klassPtr;
                            Log.Write(AppLogLevel.Debug,
                                $"MatchingProgressView klass resolved @ 0x{klassPtr:X}",
                                "MatchingProgressResolver");
                        }
                    }

                    if (SilkUtils.IsValidVirtualAddress(klassPtr))
                        viewObjectClass = gom.FindBehaviourByKlassPtr(klassPtr);
                }
                catch { }

                // Fallback: class name scan
                if (!SilkUtils.IsValidVirtualAddress(viewObjectClass))
                {
                    try
                    {
                        viewObjectClass = gom.FindBehaviourByClassName("MatchingProgressView");
                    }
                    catch
                    {
                        HandleGomFailure();
                        return 0;
                    }
                }

                if (!SilkUtils.IsValidVirtualAddress(viewObjectClass))
                {
                    HandleGomFailure();
                    return 0;
                }

                _consecutiveGomFailures = 0;

                Log.Write(AppLogLevel.Debug, $"MatchingProgressView objectClass @ 0x{viewObjectClass:X}", "MatchingProgressResolver");

                var mpPtr = Memory.ReadPtr(viewObjectClass + Offsets.MatchingProgressView._matchingProgress);
                if (!SilkUtils.IsValidVirtualAddress(mpPtr))
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

        // ─────────────────────────────────────────────────────────────────────
        // Background stage poller
        // ─────────────────────────────────────────────────────────────────────

        private static void StartStagePoller()
        {
            if (_pollerActive)
                return;

            _pollerActive = true;
            _stagePoller = new Timer(_ =>
            {
                try
                {
                    TryUpdateStage();

                    if (_consecutiveReadFailures >= ViewGoneThreshold)
                    {
                        EMatchingStage lastStage, highWater;
                        double totalElapsed;
                        int failures;

                        lock (_lock)
                        {
                            lastStage = _prevStage;
                            highWater = _highWaterStage;
                            totalElapsed = _totalSw.Elapsed.TotalSeconds;
                            failures = _consecutiveReadFailures;
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

        // ─────────────────────────────────────────────────────────────────────
        // Diagnostic snapshots
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Reads all known <c>MatchingProgress</c> fields and writes a snapshot to the log.
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

            if (!SilkUtils.IsValidVirtualAddress(mp))
                return;

            try
            {
                var currentStage = (EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStage, useCache: false);
                var currentStageGroup = (EMatchingStageGroup)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.CurrentStageGroup, useCache: false);
                var stageProgress = Memory.ReadValue<float>(mp + Offsets.MatchingProgress.CurrentStageProgress, useCache: false);
                var estimateTime = Memory.ReadValue<int>(mp + Offsets.MatchingProgress.EstimateTime, useCache: false);
                var isAbortAvailable = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.IsAbortAvailable, useCache: false);
                var blockAbortDuration = Memory.ReadValue<int>(mp + Offsets.MatchingProgress.BlockAbortAbilityDurationSeconds, useCache: false);
                var showAbortPopup = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.ShowAbortConfirmationPopup, useCache: false);
                var abortRequested = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.IsMatchingAbortRequested, useCache: false);
                var canProcessStages = Memory.ReadValue<bool>(mp + Offsets.MatchingProgress.CanProcessServerStages, useCache: false);
                var lastDelayedStage = (EMatchingStage)Memory.ReadValue<int>(mp + Offsets.MatchingProgress.LastMemorizedDelayedStage, useCache: false);
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
