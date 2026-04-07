using SDK;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using eft_dma_radar.Silk.Tarkov.Unity;
using System.Diagnostics;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Minimal raid session. Reads players (position + rotation) and raid lifecycle.
    /// Phase 1 — no loot, no exits, no quests.
    /// Mirrors WPF LocalGameWorld structure.
    /// </summary>
    internal sealed class LocalGameWorld : IDisposable
    {
        #region Fields

        private readonly ulong _base;
        private readonly CancellationToken _ct;
        private readonly RegisteredPlayers _registeredPlayers;
        private volatile bool _disposed;
        private Thread? _refreshThread;

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(133);
        private static readonly TimeSpan RaidEndedCheckInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastRaidEndedCheck = DateTime.MinValue;

        // GameWorld component extraction chain: GameObject → ComponentArray[0x58] → entry[0x18] → ObjectClass[0x20]
        private static readonly uint[] GameWorldChain = [0x58, 0x18, 0x20];

        #endregion

        #region Properties

        public string MapID { get; }
        public bool InRaid => !_disposed;
        public IReadOnlyCollection<Player.Player> Players => _registeredPlayers.ToArray();
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
        }

        #endregion

        #region Lifecycle

        /// <summary>Starts the background player refresh thread.</summary>
        public void Start()
        {
            _registeredPlayers.WaitForLocalPlayer(_ct);
            _refreshThread = new Thread(RefreshWorker) { IsBackground = true, Name = "LocalGameWorld.Refresh" };
            _refreshThread.Start();
        }

        /// <summary>Single-tick manual refresh (called from the main loop when not using a refresh thread).</summary>
        public void Refresh() { /* refresh driven by background thread */ }

        public void Dispose()
        {
            _disposed = true;
        }

        #endregion

        #region Workers

        private void RefreshWorker()
        {
            while (!_disposed && !_ct.IsCancellationRequested)
            {
                try
                {
                    _registeredPlayers.Refresh();
                    CheckRaidEnded();
                    Thread.Sleep(RefreshInterval);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Memory.GameNotRunningException)
                {
                    _disposed = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Warning, "refresh_ex", TimeSpan.FromSeconds(5),
                        $"[LocalGameWorld] Refresh error ({ex.GetType().Name}): {ex.Message}\n{ex.StackTrace}");
                    Thread.Sleep(500);
                }
            }

            _disposed = true;
        }

        #endregion

        #region Game World Scan

        private static ulong FindGameWorld()
        {
            var gom = Memory.ReadValue<SilkGOM>(Memory.GOM, false);
            var gameObject = gom.GetGameObjectByName("GameWorld");
            if (gameObject == 0) return 0;

            ulong step1, step2, step3;
            try { step1 = Memory.ReadPtr(gameObject + 0x58, false); }
            catch (Exception ex) { Log.WriteLine($"[GW] Chain[0x58] failed: {ex.Message}"); return 0; }
            try { step2 = Memory.ReadPtr(step1 + 0x18, false); }
            catch (Exception ex) { Log.WriteLine($"[GW] Chain[0x18] failed: {ex.Message}"); return 0; }
            try { step3 = Memory.ReadPtr(step2 + 0x20, false); }
            catch (Exception ex) { Log.WriteLine($"[GW] Chain[0x20] failed: {ex.Message}"); return 0; }

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

        private void CheckRaidEnded()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRaidEndedCheck) < RaidEndedCheckInterval) return;
            _lastRaidEndedCheck = now;
            try { Memory.ThrowIfNotInGame(); }
            catch (Memory.GameNotRunningException) { _disposed = true; }
        }

        #endregion
    }
}
