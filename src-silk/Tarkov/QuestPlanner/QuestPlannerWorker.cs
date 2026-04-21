using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.GameWorld.Quests;
using eft_dma_radar.Silk.Tarkov.QuestPlanner.Models;

namespace eft_dma_radar.Silk.Tarkov.QuestPlanner
{
    internal enum QuestPlannerState
    {
        Disconnected,
        Lobby,
        InRaid
    }

    /// <summary>
    /// Background orchestrator that pipelines <see cref="QuestPlannerMemoryReader"/> and
    /// <see cref="QuestPlanBuilder"/> on a ~10 s lobby poll with change detection.
    /// Suspends while in a raid or disconnected.
    /// </summary>
    internal static class QuestPlannerWorker
    {
        private static readonly TimeSpan LobbyPollInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ProfileGracePeriod = TimeSpan.FromSeconds(10);

        private static readonly ManualResetEventSlim _wake = new(false);

        private static Thread? _thread;
        private static volatile bool _shutdown;

        // ── Published state ──────────────────────────────────────────────────
        public static QuestSummary? Current { get; private set; }
        public static QuestPlannerState State { get; private set; } = QuestPlannerState.Disconnected;
        public static bool IsStale { get; private set; }

        // ── Internal state ───────────────────────────────────────────────────
        private static ulong _lastFingerprint;
        private static volatile bool _forceRecompute = true;
        private static DateTime _transitionAt = DateTime.MinValue;
        private static bool _profileWarnLogged;
        private static int _profileFailCount;

        private static ulong _cachedObjectClass;

        internal static void Start()
        {
            if (_thread is not null) return;
            _shutdown = false;
            _thread = new Thread(Worker)
            {
                IsBackground = true,
                Name = "QuestPlannerWorker"
            };
            _thread.Start();
        }

        internal static void Stop() => _shutdown = true;

        internal static void InvalidateCache()
        {
            _cachedObjectClass = 0;
            Current = null;
            _lastFingerprint = 0;
            _forceRecompute = true;
        }

        /// <summary>Forces a recompute on the next tick (e.g. settings changed).</summary>
        public static void ForceRecompute()
        {
            _forceRecompute = true;
            _wake.Set();
        }

        private static void Worker()
        {
            Log.WriteLine("[QuestPlannerWorker] Thread started.");
            while (!_shutdown)
            {
                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, "qp_worker", TimeSpan.FromSeconds(30),
                        $"[QuestPlannerWorker] Error: {ex.Message}");
                    State = QuestPlannerState.Disconnected;
                    Current = null;
                }

                _wake.Wait(1000);
                _wake.Reset();
            }
            Log.WriteLine("[QuestPlannerWorker] Thread exiting.");
        }

        private static void Tick()
        {
            // 1. DMA connection.
            if (!Memory.Ready)
            {
                if (State != QuestPlannerState.Disconnected)
                {
                    State = QuestPlannerState.Disconnected;
                    Current = null;
                    IsStale = false;
                    _forceRecompute = true;
                    _transitionAt = DateTime.UtcNow;
                    _profileWarnLogged = false;
                    _profileFailCount = 0;
                }
                return;
            }

            // 2. In-raid / hideout — planner is a lobby tool.
            if (Memory.InRaid || Memory.InHideout)
            {
                if (State != QuestPlannerState.InRaid)
                {
                    State = QuestPlannerState.InRaid;
                    Current = null;
                    IsStale = false;
                    _forceRecompute = true;
                    _transitionAt = DateTime.UtcNow;
                    _profileWarnLogged = false;
                    _profileFailCount = 0;
                }
                return;
            }

            State = QuestPlannerState.Lobby;

            // 3. Resolve profile pointer.
            var profile = LobbyProfileResolver.Resolve(ref _cachedObjectClass);
            if (profile == 0)
            {
                if (DateTime.UtcNow - _transitionAt < ProfileGracePeriod) return;

                _profileFailCount++;
                if (!_profileWarnLogged)
                {
                    Log.WriteLine("[QuestPlannerWorker] Profile not available — waiting for lobby.");
                    _profileWarnLogged = true;
                    IsStale = Current is not null;
                }

                var backoff = Math.Min(_profileFailCount * 2000, 10000);
                _wake.Wait(backoff);
                _wake.Reset();
                return;
            }

            if (_profileWarnLogged)
            {
                Log.WriteLine("[QuestPlannerWorker] Profile available — resuming quest planning.");
                _profileWarnLogged = false;
            }
            _profileFailCount = 0;
            IsStale = false;

            // 4. Task metadata must be loaded before we can plan.
            if (EftDataManager.TaskData is null || EftDataManager.TaskData.Count == 0)
            {
                return;
            }

            // 5. Read quest state.
            var quests = QuestPlannerMemoryReader.ReadAvailableQuests(profile);

            // 6. Change detection via cheap order-independent fingerprint.
            var fingerprint = ComputeFingerprint(quests);
            if (!_forceRecompute && fingerprint == _lastFingerprint)
            {
                _wake.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
                _wake.Reset();
                return;
            }

            var settings = new QuestPlannerSettings
            {
                KappaFilter = SilkProgram.Config.QuestKappaFilter
            };

            var summary = QuestPlanBuilder.GetSummary(quests, EftDataManager.TaskData, settings);
            Current = summary;
            _lastFingerprint = fingerprint;
            _forceRecompute = false;

            Log.WriteLine($"[QuestPlannerWorker] Plan computed: {summary.Maps.Count} maps, {summary.TotalCompletableObjectives} objectives.");

            _wake.Wait((int)LobbyPollInterval.TotalMilliseconds - 1000);
            _wake.Reset();
        }

        /// <summary>
        /// Order-independent 64-bit fingerprint of the quest state. Combines quest IDs,
        /// status buckets, and completed-condition IDs via XOR so allocation-free diffs
        /// can be performed on every tick. Collisions are astronomically unlikely for
        /// realistic quest counts.
        /// </summary>
        private static ulong ComputeFingerprint(AvailableQuests quests)
        {
            ulong fp = 0;
            fp ^= BucketFingerprint(quests.Started, 2);
            fp ^= BucketFingerprint(quests.AvailableForStart, 1);
            fp ^= BucketFingerprint(quests.AvailableForFinish, 3);
            return fp;
        }

        private static ulong BucketFingerprint(List<QuestData> list, int statusTag)
        {
            ulong bucket = (ulong)statusTag * 0x9E3779B97F4A7C15UL;
            for (int i = 0; i < list.Count; i++)
            {
                var q = list[i];
                ulong qfp = Mix((ulong)q.Id.GetHashCode() | ((ulong)statusTag << 56));
                foreach (var cond in q.CompletedConditions)
                    qfp ^= Mix((ulong)cond.GetHashCode());
                bucket ^= qfp;
            }
            return bucket;
        }

        private static ulong Mix(ulong x)
        {
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb9fe1a85ec53UL;
            x ^= x >> 33;
            return x;
        }
    }
}
