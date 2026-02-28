using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.API;

namespace eft_dma_radar.Tarkov.GameWorld
{
    /// <summary>
    /// Class containing Game (Raid) instance.
    /// IDisposable.
    /// </summary>
    public sealed class LocalGameWorld : IDisposable
    {
        #region Fields / Properties / Constructors

        public static implicit operator ulong(LocalGameWorld x) => x.Base;

        /// <summary>
        /// LocalGameWorld Address.
        /// </summary>
        public ulong Base { get; }

        private static Config Config => Program.Config;

        private static readonly WaitTimer _refreshWait = new();

        private readonly CancellationTokenSource _cts = new();
        private RegisteredPlayers _rgtPlayers;
        private LootManager _lootManager;
        private ExitManager _exfilManager;
        private ExplosivesManager _grenadeManager;
        private WorldInteractablesManager _worldInteractablesManager;
        public WorldInteractablesManager Interactables => _worldInteractablesManager;

        private Thread _t1;
        private Thread _t2;
        private Thread _t3;
        private Thread _t4;
        private Thread _t5;

        /// <summary>
        /// Map ID of Current Map (captured when GameWorld is found).
        /// </summary>
        public string MapID { get; }

        public static bool IsOffline { get; private set; }
        public static ulong LevelSettings { get; private set; }

        private bool _disposed;
        private bool _raidStarted;
        private int _mapCheckTick;

        public bool InRaid => !_disposed;
        public IReadOnlyCollection<Player> Players => _rgtPlayers;
        public IReadOnlyCollection<IExplosiveItem> Explosives => _grenadeManager;
        public IReadOnlyCollection<IExitPoint> Exits => _exfilManager;
        public LocalPlayer LocalPlayer => _rgtPlayers?.LocalPlayer;
        public LootManager Loot => _lootManager;

        public QuestManager QuestManager { get; private set; }

        public CameraManager CameraManager { get; private set; }

        /// <summary>
        /// True if raid instance is still active, and safe to Write Memory.
        /// </summary>
        public bool IsSafeToWriteMem
        {
            get
            {
                try
                {
                    if (!InRaid)
                        return false;
                    return IsRaidActive();
                }
                catch
                {
                    return false;
                }
            }
        }

        static LocalGameWorld()
        {
            MemDMABase.GameStopped += Memory_GameStopped;
        }

        private static void Memory_GameStopped(object sender, EventArgs e)
        {
            _screenManagerStaticClass = 0;
            LevelSettings = 0;
            LevelSettingsResolver.Reset();
            Il2CppClass.ForceReset();      // <?? REQUIRED
        }

        /// <summary>
        /// Game Constructor - Phase 1: Minimal initialization.
        /// Player/Loot allocation is deferred until WaitForRaidReady().
        /// </summary>
        private LocalGameWorld(ulong localGameWorld, string mapID)
        {
            Base = localGameWorld;
            MapID = mapID;

            // Reset static assets for a new raid/game.
            Player.Reset();
        }

        /// <summary>
        /// Phase 2: Wait for raid to be fully ready, then initialize players/loot.
        /// This ensures we don't read garbage transform data from menu/loading state.
        /// </summary>
        private void WaitForRaidReady(CancellationToken ct)
        {
            XMLogging.WriteLine("[Raid] Waiting for raid to be fully ready...");

            // Phase 1: Wait for camera to be available
            const int maxCameraAttempts = 60; // 30 seconds max wait
            int attempts = 0;
            while (attempts++ < maxCameraAttempts)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    CameraManager.Initialize();
                    CameraManager = new CameraManager();
                    CameraManager.FPSCamera.ThrowIfInvalidVirtualAddress();
                    XMLogging.WriteLine("[Raid] Camera detected!");
                    break;
                }
                catch
                {
                    CameraManager = null;
                    if (attempts % 10 == 0)
                        XMLogging.WriteLine($"[Raid] Still waiting for camera... attempt {attempts}/{maxCameraAttempts}");
                    ct.WaitHandle.WaitOne(500);
                }
            }

            XMLogging.WriteLine("[Raid] Waiting for LocalPlayer to be fully in raid...");

            // Phase 2: Wait for LocalPlayer to be valid (RegisteredPlayers list populated)
            const int maxPlayerAttempts = 60; // 30 seconds max wait
            attempts = 0;
            while (attempts++ < maxPlayerAttempts)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (IsLocalPlayerInRaid())
                    {
                        XMLogging.WriteLine("[Raid] LocalPlayer confirmed in raid!");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (attempts % 10 == 0)
                        XMLogging.WriteLine($"[Raid] Waiting... ({ex.Message})");
                }

                ct.WaitHandle.WaitOne(500);
            }

            if (attempts >= maxPlayerAttempts)
                XMLogging.WriteLine("[Raid] Timeout waiting for raid confirmation, proceeding anyway...");

            InitializeGameData(ct);
            XMLogging.WriteLine("[Raid] Waiting for real raid start...");

            if (IsInRealRaid())
                XMLogging.WriteLine("[Raid] Raid fully active!");
        }

        /// <summary>
        /// Check if LocalPlayer is actually in raid by validating player data is readable.
        /// We check RegisteredPlayers count > 0 (like PWA does) instead of requiring a held item.
        /// This allows entering raid without a weapon (hatchet runs, etc.)
        /// </summary>
        private bool IsLocalPlayerInRaid()
        {
            // Read MainPlayer directly (LocalPlayer)
            var playerBase = Memory.ReadPtr(Base + Offsets.ClientLocalGameWorld.MainPlayer, false);
            if (playerBase == 0 || !playerBase.IsValidVirtualAddress())
                return false;

            // Read RegisteredPlayers and check count (PWA's approach)
            var rgtPlayersAddr = Memory.ReadPtr(Base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
            if (rgtPlayersAddr == 0 || !rgtPlayersAddr.IsValidVirtualAddress())
                return false;

            // Read list count - if we have players, we're in raid
            var listBase = Memory.ReadPtr(rgtPlayersAddr + UnityOffsets.ManagedList.ItemsPtr, false);
            var playerCount = Memory.ReadValue<int>(rgtPlayersAddr + UnityOffsets.ManagedList.Count, false);

            if (playerCount < 1 || playerCount > 100) // Sanity check
                return false;

            // Validate first player entry exists
            var firstPlayer = Memory.ReadPtr(listBase + UnityOffsets.ManagedArray.FirstElement, false);
            if (firstPlayer == 0 || !firstPlayer.IsValidVirtualAddress())
                return false;

            XMLogging.WriteLine($"[Raid] RegisteredPlayers validated: {playerCount} player(s)");
            return true;
        }

        /// <summary>
        /// Initialize players, loot, and other game data.
        /// Only called after raid is confirmed ready.
        /// </summary>
        private void InitializeGameData(CancellationToken ct)
        {
            XMLogging.WriteLine("[Raid] Initializing game data...");

            var rgtPlayersAddr = Memory.ReadPtr(Base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
            _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
            if (_rgtPlayers.GetPlayerCount() < 1)
                throw new ArgumentOutOfRangeException(nameof(_rgtPlayers));

            _lootManager = new LootManager(Base, ct);
            _exfilManager = new ExitManager(Base, _rgtPlayers.LocalPlayer.IsPmc);
            _grenadeManager = new ExplosivesManager(Base);
            XMLogging.WriteLine($"[WorldInteractablesManager] Calling from LocalGameWorld: 0x{Base:X}");
            _worldInteractablesManager = new WorldInteractablesManager(Base);

            XMLogging.WriteLine("[Raid] Game data initialized successfully!");

            if (Config.MemWrites.Aimbot.Enabled && Config.MemWrites.MemWritesEnabled)
            {
                Task.Run(() =>
                {
                    try
                    {
                        Features.MemoryWrites.Aimbot.RunBallisticsDiagnosticOnce();
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[Raid] Ballistics diagnostic failed: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Start all Game Threads.
        /// </summary>
        public void Start()
        {
            var ct = _cts.Token;

            _t1 = new Thread(() => RealtimeWorker(ct))
            {
                IsBackground = true
            };
            _t2 = new Thread(() => MiscWorker(ct))
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _t3 = new Thread(() => GrenadesWorker(ct))
            {
                IsBackground = true
            };
            _t4 = new Thread(() => FastWorker(ct))
            {
                IsBackground = true
            };
            _t5 = new Thread(() => InteractablesWorker(ct))
            {
                IsBackground = true
            };

            _t1.Start();
            _t2.Start();
            _t3.Start();
            _t4.Start();
            _t5.Start();
        }

        /// <summary>
        /// Blocks until a LocalGameWorld Singleton Instance can be instantiated.
        /// Waits for raid to be fully ready before initializing player data.
        /// </summary>
        /// <param name="ct">Cancellation token for clean restart.</param>
        public static LocalGameWorld CreateGameInstance(CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                RaidCooldown.WaitIfActive(ct);
                ResourceJanitor.Run();
                Memory.ThrowIfNotInGame();

                try
                {
                    // Phase 1: Find GameWorld (minimal init)
                    var instance = GetLocalGameWorld(ct);

                    // Phase 2: Wait for raid to be ready, then initialize game data
                    instance.WaitForRaidReady(ct);

                    XMLogging.WriteLine("Raid has started!");
                    return instance;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"ERROR Instantiating Game Instance: Probably not in Raid, waiting... {ex}");
                }
                finally
                {
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Checks if a Raid has started and loads LocalGameWorld using IL2CPP.
        /// Replaces old Mono singleton approach with GOM linked list iteration.
        /// </summary>
        /// <param name="ct">Cancellation token for clean restart.</param>
        private static LocalGameWorld GetLocalGameWorld(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
        
            try
            {
                // Use IL2CPP GameObjectManager to find GameWorld (Mono is deprecated)
                var gomAddress = Memory.GOM;
                if (!gomAddress.IsValidVirtualAddress())
                    throw new InvalidOperationException("Invalid GOM address");
        
                // Find GameWorld via IL2CPP GOM iteration with parallel search
                var localGameWorld = GameWorldExtensions.GetGameWorld(gomAddress, ct, out string map);
                if (!localGameWorld.IsValidVirtualAddress())
                    throw new InvalidOperationException("Invalid LocalGameWorld address");
        
                // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
                // OFFLINE / ONLINE detection (cheap)
                // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
                try
                {
                    ulong classNamePtr = Memory.ReadPtrChain(
                        localGameWorld,
                        UnityOffsets.Component.To_NativeClassName,
                        useCache: false);
        
                    string className = Memory.ReadString(classNamePtr, 64, useCache: false);
        
                    IsOffline = className.Equals(
                        "ClientLocalGameWorld",
                        StringComparison.OrdinalIgnoreCase);
        
                    XMLogging.WriteLine($"[IL2CPP] Raid Mode: {(IsOffline ? "OFFLINE" : "ONLINE")}");
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[IL2CPP] Could not detect offline mode: {ex.Message}");
                    IsOffline = false;
                }
        
                // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
                // LEVEL SETTINGS กงC non-blocking
                // ?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่?ก่
                try
                {
                    // 1) Fast path: use cached value if we already resolved it
                    if (LevelSettingsResolver.TryGetCached(out var cached) &&
                        cached.IsValidVirtualAddress())
                    {
                        LevelSettings = cached;
                    }
                    else
                    {
                        // 2) No cached value yet กงC schedule a background resolve.
                        //    Do NOT block the game / raid init thread here.
                        LevelSettings = 0;
        
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                var ls = LevelSettingsResolver.GetLevelSettings();
                                if (ls.IsValidVirtualAddress())
                                {
                                    LevelSettings = ls;
                                    XMLogging.WriteLine($"[IL2CPP] LevelSettings resolved async @ 0x{ls:X}");
                                }
                            }
                            catch (Exception ex2)
                            {
                                XMLogging.WriteLine($"[IL2CPP] Async LevelSettings resolve failed: {ex2.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[IL2CPP] LevelSettings resolution error: {ex.Message}");
                    LevelSettings = 0;
                }
        
                return new LocalGameWorld(localGameWorld, map);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR Getting LocalGameWorld via IL2CPP", ex);
            }
        }


        /// <summary>
        /// Main Game Loop executed by Memory Worker Thread. Refreshes/Updates Player List and performs Player Allocations.
        /// </summary>
        public void Refresh()
        {
            try
            {
                ThrowIfRaidEnded();

                if (MapID.Equals("tarkovstreets", StringComparison.OrdinalIgnoreCase) ||
                    MapID.Equals("woods", StringComparison.OrdinalIgnoreCase))
                {
                    TryAllocateBTR();
                }

                _rgtPlayers.Refresh(); // Check for new players, add to list, etc.
            }
            catch (RaidEnded)
            {
                NotificationsShared.Info("Raid has ended!");
                XMLogging.WriteLine("Raid has ended!");
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                LevelSettingsResolver.Reset();
                EftHardSettingsResolver.InvalidateCache();
                EftWeatherControllerResolver.InvalidateCache();

                Il2CppClass.ForceReset();

                Dispose();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR - Raid ended due to unhandled exception: {ex}");
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                throw;
            }
        }

        public static class RaidCooldown
        {
            private static readonly object _lock = new();
            private static DateTime _nextAllowed = DateTime.MinValue;

            public static void BeginCooldown(int seconds = 12)
            {
                lock (_lock)
                {
                    _nextAllowed = DateTime.UtcNow.AddSeconds(seconds);
                }
            }

            public static void WaitIfActive(CancellationToken ct)
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    if (now < _nextAllowed)
                    {
                        var waitMs = (int)(_nextAllowed - now).TotalMilliseconds;
                        XMLogging.WriteLine($"[RaidCooldown] Waiting {waitMs} ms before next raid init...");
                        Monitor.Exit(_lock);
                        try
                        {
                            ct.WaitHandle.WaitOne(waitMs);
                        }
                        finally
                        {
                            Monitor.Enter(_lock);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Throws an exception if the current raid instance has ended.
        /// </summary>
        /// <exception cref="RaidEnded"></exception>
        private void ThrowIfRaidEnded()
        {
            for (int i = 0; i < 5; i++) // Re-attempt if read fails -- 5 times
            {
                try
                {
                    if (!IsRaidActive())
                    {
                        LevelSettings = 0;
                        LevelSettingsResolver.Reset();
                        Il2CppClass.ForceReset();
                        GuardManager.ClearCache();
                        LootFilterControl.RemoveNonStaticGroups();
                        LootItem.ClearNotificationHistory();
                        throw new Exception("Not in raid!");
                    }
                    return;
                }
                catch
                {
                    Thread.Sleep(10); // short delay between read attempts
                }
            }
            throw new RaidEnded(); // Still not valid? Raid must have ended.
        }

        /// <summary>
        /// Checks if the Current Raid is Active, and LocalPlayer is alive/active.
        /// </summary>
        /// <returns>True if raid is active, otherwise False.</returns>
        private bool IsRaidActive()
        {
            try
            {
                // 1) MainPlayer sanity
                var mainPlayer = Memory.ReadPtr(this + Offsets.ClientLocalGameWorld.MainPlayer, false);
                if (!mainPlayer.IsValidVirtualAddress())
                    return false;

                ArgumentOutOfRangeException.ThrowIfNotEqual(mainPlayer, _rgtPlayers.LocalPlayer, nameof(mainPlayer));

                // 2) Player count sanity
                if (_rgtPlayers.GetPlayerCount() <= 0)
                    return false;

                // 3) Map transition detection กงC but not on every single call
                if ((_mapCheckTick++ & 0x3F) == 0) // every 64 calls
                {
                    var currentMapId = GetCurrentMapId();
                    if (!string.IsNullOrEmpty(currentMapId) &&
                        !string.IsNullOrEmpty(MapID) &&
                        !string.Equals(currentMapId, MapID, StringComparison.Ordinal))
                    {
                        XMLogging.WriteLine($"[Raid] Map changed: '{MapID}' -> '{currentMapId}'. Marking raid as ended.");
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                LootFilterControl.RemoveNonStaticGroups();
                LootItem.ClearNotificationHistory();
                return false;
            }
        }

        #endregion

        #region Raid Started Status

        private string GetCurrentMapId()
        {
            try
            {
                // Same logic you used in GetLocalGameWorld()
                var locationIdPtr = Memory.ReadValue<ulong>(Base + Offsets.ClientLocalGameWorld.LocationId, false);

                if (locationIdPtr == 0)
                {
                    // Offline fallback (same as GetGameWorld)
                    var localPlayer = Memory.ReadPtr(Base + Offsets.ClientLocalGameWorld.MainPlayer, false);
                    if (!localPlayer.IsValidVirtualAddress())
                        return null;

                    var locationPtr = Memory.ReadPtr(localPlayer + Offsets.Player.Location, false);
                    if (!locationPtr.IsValidVirtualAddress())
                        return null;

                    return Memory.ReadUnityString(locationPtr, 128, false);
                }

                return Memory.ReadUnityString(locationIdPtr, 128, false);
            }
            catch
            {
                return null;
            }
        }

        public bool RaidHasStarted => _raidStarted;

        private static ulong _screenManagerStaticClass;

        /// <summary>
        /// Checks if the Raid has started (players can move about).
        /// Pure check + one-shot side effect when raid first becomes "real".
        /// </summary>
        public bool IsInRealRaid()
        {
            try
            {
                var local = LocalPlayer; // from this LocalGameWorld
                if (local is null)
                {
                    XMLogging.WriteLine("Not Fully in raid yet (no LocalPlayer)...");
                    return false;
                }

                ulong handsController = local.Firearm.HandsController.Item1;
                if (!Utils.IsValidVirtualAddress(handsController))
                {
                    XMLogging.WriteLine("Not Fully in raid yet (hands controller invalid)...");
                    return false;
                }

                if (!_raidStarted)
                {
                    _raidStarted = true;

                    // Fire feature hooks async so we don't block the caller
                    Task.Run(() =>
                    {
                        foreach (var feature in IFeature.AllFeatures)
                        {
                            try
                            {
                                feature.OnRaidStart();
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"[Raid] OnRaidStart error in {feature.GetType().Name}: {ex}");
                            }
                        }
                        foreach (var player in Memory.Players)
                        {
                            if(player is null)
                                continue;
                            try
                            {
                                
                                PlayerLookupApiClient.TryResolve(player);
                                XMLogging.WriteLine($"[Raid] PlayerLookupApiClient resolved player {player.ProfileID}");
                            }
                            catch (Exception ex)
                            {
                                XMLogging.WriteLine($"[Raid] OnRaidStart error in Player {player}: {ex}");
                            }
                        }
                        XMLogging.WriteLine("[Raid] Raid fully active, all features notified.");
                    });
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Realtime Thread T1

        /// <summary>
        /// Managed Thread that does realtime (player position/info) updates.
        /// </summary>
        private void RealtimeWorker(CancellationToken ct) // t1
        {
            if (_disposed) return;
            try
            {
                XMLogging.WriteLine("Realtime thread starting...");
                while (InRaid)
                {
                    if (Config.RatelimitRealtimeReads ||!CameraManagerBase.EspRunning || (MemWriteFeature<Aimbot>.Instance.Enabled && Aimbot.Engaged))
                    {
                        _refreshWait.AutoWait(TimeSpan.FromMilliseconds(1), 1000);
                    }

                    ct.ThrowIfCancellationRequested();
                    RealtimeLoop(); // Realtime update loop (player positions, etc.)
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR on Realtime Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                XMLogging.WriteLine("Realtime thread stopping...");
            }
        }

        /// <summary>
        /// Updates all Realtime Values (View Matrix, player positions, etc.)
        /// </summary>
        private void RealtimeLoop()
        {
            try
            {
                var players = _rgtPlayers.Where(x => x.IsActive && x.IsAlive);
                var localPlayer = LocalPlayer;
                if (!players.Any()) // No players - Throttle
                {
                    Thread.Sleep(1);
                    return;
                }

                using var scatterMap = ScatterReadMap.Get();
                var round1 = scatterMap.AddRound(false);
                if (CameraManager is CameraManager cm)
                {
                    cm.OnRealtimeLoop(round1[-1], localPlayer);
                }

                int i = 0;
                foreach (var player in players)
                {
                    player.OnRealtimeLoop(round1[i++]);
                }

                scatterMap.Execute(); // Execute scatter read
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR - UpdatePlayers Loop FAILED: {ex}");
            }
        }

        #endregion

        #region Misc Thread T2

        /// <summary>
        /// Managed Thread that does Misc. Local Game World Updates.
        /// </summary>
        private void MiscWorker(CancellationToken ct) // t2
        {
            if (_disposed) return;
            try
            {
                XMLogging.WriteLine("Misc thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    UpdateMisc();
                    Thread.Sleep(50);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR on Misc Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                XMLogging.WriteLine("Misc thread stopping...");
            }
        }

        /// <summary>
        /// Validates Player Transforms -> Checks Exfils -> Checks Loot -> Checks Quests
        /// </summary>
        private void UpdateMisc()
        {
            ValidatePlayerTransforms(); // Check for transform anomalies

            // Refresh exfils
            _exfilManager.Refresh();

            // Refresh Loot
            _lootManager.Refresh();

            if (Config.LootWishlist)
            {
                try
                {
                    Memory.LocalPlayer?.RefreshWishlist();
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Wishlist] ERROR Refreshing: {ex}");
                }
            }

            RefreshGear(); // Update gear periodically

            if (Config.QuestHelper.Enabled)
            {
                try
                {
                    if (QuestManager is null)
                    {
                        var localPlayer = LocalPlayer;
                        if (localPlayer is not null)
                            QuestManager = new QuestManager(localPlayer.Profile);
                    }
                    else
                    {
                        QuestManager.Refresh();
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[QuestManager] CRITICAL ERROR: {ex}");
                }
            }
        }

        /// <summary>
        /// Refresh Gear Manager
        /// </summary>
        private void RefreshGear()
        {
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsHostileActive);
                if (players is not null && players.Any())
                {
                    foreach (var player in players)
                        player.RefreshGear();
                }
            }
            catch
            {
            }
        }

        public void ValidatePlayerTransforms()
        {
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsActive && x.IsAlive && x is not BtrOperator);
                if (players.Any()) // at least 1 player
                {
                    using var scatterMap = ScatterReadMap.Get();
                    var round1 = scatterMap.AddRound();
                    var round2 = scatterMap.AddRound();
                    int i = 0;
                    foreach (var player in players)
                    {
                        player.OnValidateTransforms(round1[i], round2[i]);
                        i++;
                    }
                    scatterMap.Execute(); // execute scatter read
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR - ValidatePlayerTransforms Loop FAILED: {ex}");
            }
        }

        #endregion

        #region Grenades Thread T3

        /// <summary>
        /// Managed Thread that does Grenade/Throwable updates.
        /// </summary>
        private void GrenadesWorker(CancellationToken ct) // t3
        {
            if (_disposed) return;
            try
            {
                XMLogging.WriteLine("Grenades thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    _grenadeManager.Refresh();
                    Thread.Sleep(10);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR on Grenades Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                XMLogging.WriteLine("Grenades thread stopping...");
            }
        }

        #endregion

        #region Fast Thread T4

        /// <summary>
        /// Managed Thread that does Hands Manager / DMA Toolkit updates.
        /// No long operations on this thread.
        /// </summary>
        private void FastWorker(CancellationToken ct) // t4
        {
            if (_disposed) return;
            try
            {
                XMLogging.WriteLine("FastWorker thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    RefreshCameraManager();
                    RefreshFast();
                    Thread.Sleep(100);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR on FastWorker Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                XMLogging.WriteLine("FastWorker thread stopping...");
            }
        }

        private void InteractablesWorker(CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                XMLogging.WriteLine("Interactables thread starting...");
                while (InRaid)
                {
                    ct.ThrowIfCancellationRequested();
                    RefreshWorldInteractables();
                    Thread.Sleep(750);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CRITICAL ERROR on Interactables Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                XMLogging.WriteLine("Interactables thread stopping...");
            }
        }

        private void RefreshCameraManager()
        {
            try
            {
                CameraManager ??= new CameraManager();
            }
            catch
            {
                // Swallow กงC camera can fail transiently during transitions
            }
        }

        private void RefreshWorldInteractables()
        {
            _worldInteractablesManager.Refresh();
        }

        /// <summary>
        /// Refresh various player items via Fast Worker Thread.
        /// </summary>
        private void RefreshFast()
        {
            try
            {
                var players = _rgtPlayers
                    .Where(x => x.IsActive && x.IsAlive);
                if (players is not null && players.Any())
                {
                    foreach (var player in players)
                    {
                        player.RefreshHands();
                        if (player is LocalPlayer localPlayer)
                            localPlayer.Firearm.Update();
                    }
                }
            }
            catch
            {
            }
        }

        #endregion

        #region BTR Vehicle

        /// <summary>
        /// Checks if there is a Bot attached to the BTR Turret and re-allocates the player instance.
        /// </summary>
        public void TryAllocateBTR()
        {
            try
            {
                var btrController = Memory.ReadPtr(this + Offsets.ClientLocalGameWorld.BtrController);
                var btrView = Memory.ReadPtr(btrController + Offsets.BtrController.BtrView);
                var btrTurretView = Memory.ReadPtr(btrView + Offsets.BTRView.turret);
                var btrOperator = Memory.ReadPtr(btrTurretView + Offsets.BTRTurretView.AttachedBot);
                _rgtPlayers.TryAllocateBTR(btrView, btrOperator);
            }
            catch
            {
                // optional logging
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            bool alreadyDisposed = Interlocked.Exchange(ref _disposed, true);
            if (!alreadyDisposed)
            {
                XMLogging.WriteLine("[Raid] LocalGameWorld disposed ?? entering cooldown.");

                foreach (var feature in IFeature.AllFeatures)
                {
                    try
                    {
                        feature.OnRaidEnd();
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[Raid] OnRaidEnd error in {feature.GetType().Name}: {ex}");
                    }
                }

                _raidStarted = false;

                LevelSettings = 0;
                LevelSettingsResolver.Reset();
                EftHardSettingsResolver.InvalidateCache();
                EftWeatherControllerResolver.InvalidateCache();

                Il2CppClass.ForceReset();
                _screenManagerStaticClass = 0;

                // 10กงC15 seconds cooldown recommended
                RaidCooldown.BeginCooldown(12);

                _cts.Cancel();
                _cts.Dispose();
            }
        }

        #endregion

        #region Types

        public sealed class RaidEnded : Exception
        {
            public RaidEnded()
            {
            }

            public RaidEnded(string message)
                : base(message)
            {
            }

            public RaidEnded(string message, Exception inner)
                : base(message, inner)
            {
            }
        }

        #endregion
    }
}