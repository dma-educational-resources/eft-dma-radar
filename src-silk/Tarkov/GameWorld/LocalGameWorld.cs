using eft_dma_radar.Silk.Misc.Workers;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Minimal raid session. Reads players (position + rotation) and raid lifecycle.
    /// Phase 1 — no loot, no exits, no quests.
    /// <para>
    /// Worker thread model:
    /// <list type="bullet">
    ///   <item><b>RealtimeWorker</b> (8ms target, DynamicSleep, AboveNormal priority) — scatter-batched
    ///   position + rotation for all active players in a single DMA round-trip.
    ///   Actual sleep = max(0, 8ms - workTime).</item>
    ///   <item><b>RegistrationWorker</b> (100ms, BelowNormal priority) — player list discovery,
    ///   lifecycle management, transform validation, raid-ended checks.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class LocalGameWorld : IDisposable
    {
        #region Fields

        private readonly ulong _base;
        private readonly CancellationToken _ct;
        private readonly RegisteredPlayers _registeredPlayers;
        private volatile bool _disposed;
        private WorkerThread? _realtimeWorker;
        private WorkerThread? _registrationWorker;

        private static readonly TimeSpan TransformValidationInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RaidEndedCheckInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastRaidEndedCheck = DateTime.MinValue;
        private DateTime _lastTransformValidation = DateTime.UtcNow;

        #endregion

        #region Properties

        public string MapID { get; }
        public bool InRaid => !_disposed;
        public RegisteredPlayers RegisteredPlayers => _registeredPlayers;
        public Player.Player? LocalPlayer => _registeredPlayers.LocalPlayer;

        #endregion

        #region Factory

        /// <summary>
        /// Scans the GOM for a live LocalGameWorld and creates a LocalGameWorld from it.
        /// Blocks until found or throws if the game process is gone.
        /// </summary>
        public static LocalGameWorld Create(CancellationToken ct)
        {
            var processCheckSw = Stopwatch.StartNew();

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Rate-limit the expensive FullRefresh+PID check to once per 5s
                if (processCheckSw.ElapsedMilliseconds >= 5000)
                {
                    processCheckSw.Restart();
                    Memory.ThrowIfNotInGame();
                }

                try
                {
                    var gameWorld = FindGameWorld();
                    if (gameWorld == 0)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    // Validate we are actually in a raid: MainPlayer must be a valid pointer
                    if (!Memory.TryReadPtr(gameWorld + Offsets.ClientLocalGameWorld.MainPlayer, out var mainPlayerPtr, false)
                        || mainPlayerPtr == 0)
                    {
                        Log.WriteRateLimited(AppLogLevel.Info, "gw_search", TimeSpan.FromSeconds(5),
                            "[LocalGameWorld] GameWorld found but no MainPlayer yet — waiting for raid...");
                        Thread.Sleep(500);
                        continue;
                    }

                    var mapId = ReadMapID(gameWorld);
                    Log.WriteLine($"[LocalGameWorld] Found GameWorld @ 0x{gameWorld:X}, map = '{mapId}'");
                    return new LocalGameWorld(gameWorld, mapId, ct);
                }
                catch (Memory.GameNotRunningException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Info, "gw_search", TimeSpan.FromSeconds(5),
                        $"[LocalGameWorld] Waiting for raid... ({ex.Message})");
                    Thread.Sleep(500);
                }
            }
        }

        private LocalGameWorld(ulong gameWorldBase, string mapId, CancellationToken ct)
        {
            _base = gameWorldBase;
            MapID = mapId;
            _ct = ct;
            _registeredPlayers = new RegisteredPlayers(gameWorldBase);

            _realtimeWorker = new WorkerThread
            {
                Name = "Realtime Worker",
                ThreadPriority = ThreadPriority.AboveNormal,
                SleepDuration = TimeSpan.FromMilliseconds(8),
                SleepMode = WorkerSleepMode.DynamicSleep
            };
            _realtimeWorker.PerformWork += RealtimeWorker_PerformWork;

            _registrationWorker = new WorkerThread
            {
                Name = "Registration Worker",
                ThreadPriority = ThreadPriority.BelowNormal,
                SleepDuration = TimeSpan.FromMilliseconds(100),
                SleepMode = WorkerSleepMode.Default
            };
            _registrationWorker.PerformWork += RegistrationWorker_PerformWork;
        }

        #endregion

        #region Lifecycle

        /// <summary>Starts the background worker threads.</summary>
        public void Start()
        {
            _registeredPlayers.WaitForLocalPlayer(_ct);
            _realtimeWorker?.Start();
            _registrationWorker?.Start();
        }

        /// <summary>Single-tick manual refresh (called from the main loop when not using a refresh thread).</summary>
        public void Refresh() { /* refresh driven by WorkerThreads */ }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _realtimeWorker?.Dispose();
            _registrationWorker?.Dispose();
            _realtimeWorker = null;
            _registrationWorker = null;
        }

        #endregion

        #region Workers

        /// <summary>
        /// Realtime work tick (DynamicSleep: 8ms target, AboveNormal priority).
        /// Scatter-batched position + rotation reads — single DMA round-trip per tick.
        /// </summary>
        private void RealtimeWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed) return;
            _registeredPlayers.UpdateRealtimeData();
        }

        /// <summary>
        /// Registration work tick (100ms, BelowNormal priority).
        /// Player list discovery, lifecycle, transform validation, raid-ended checks.
        /// </summary>
        private void RegistrationWorker_PerformWork(CancellationToken ct)
        {
            if (_disposed) return;

            _registeredPlayers.RefreshRegistration();

            // Periodic transform validation
            var now = DateTime.UtcNow;
            if ((now - _lastTransformValidation) >= TransformValidationInterval)
            {
                _lastTransformValidation = now;
                _registeredPlayers.ValidateTransforms();
            }

            // Periodic raid-ended check
            if ((now - _lastRaidEndedCheck) >= RaidEndedCheckInterval)
            {
                _lastRaidEndedCheck = now;
                try { Memory.ThrowIfNotInGame(); }
                catch (Memory.GameNotRunningException) { _disposed = true; }
            }
        }

        #endregion

        #region Game World Scan

        private static ulong FindGameWorld()
        {
            var gom = Memory.ReadValue<SilkGOM>(Memory.GOM, false);
            var gameObject = gom.GetGameObjectByName("GameWorld");
            if (gameObject == 0) return 0;

            // GameObject → ComponentArray → entry → ObjectClass (GameWorld instance)
            ulong step1, step2, step3;
            try { step1 = Memory.ReadPtr(gameObject + 0x58, false); }
            catch { return 0; }
            try { step2 = Memory.ReadPtr(step1 + 0x18, false); }
            catch { return 0; }
            try { step3 = Memory.ReadPtr(step2 + 0x20, false); }
            catch { return 0; }

            return step3;
        }

        private static string ReadMapID(ulong gameWorld)
        {
            try
            {
                var locationIdPtr = Memory.ReadPtr(gameWorld + Offsets.ClientLocalGameWorld.LocationId, false);
                return Memory.ReadUnityString(locationIdPtr, 64, false);
            }
            catch
            {
                return "unknown";
            }
        }

        #endregion
    }
}
