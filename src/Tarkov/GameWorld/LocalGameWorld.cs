using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.API;
using eft_dma_radar.Web.ProfileApi;

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
        public static ulong MatchingProgress { get; private set; }

        /// <summary>
        /// Address of the last disposed LocalGameWorld instance.
        /// Used to reject stale GameWorld objects that Unity keeps alive
        /// in the scene graph after a raid ends (post-raid menu).
        /// Accessed via <see cref="Interlocked"/> (ulong cannot be volatile).
        /// </summary>
        private static ulong _lastDisposedBase;

        /// <summary>
        /// When set, <see cref="Dispose"/> will NOT record <see cref="Base"/> into
        /// <see cref="_lastDisposedBase"/>.  This allows a user-initiated restart
        /// to re-detect the same (still-live) GameWorld.
        /// Accessed via <see cref="Interlocked"/>.
        /// </summary>
        private static int _suppressStaleGuard;

        private bool _disposed;
        private bool _raidStarted;
        private int _mapCheckTick;
        private readonly List<Player> _realtimeScratch = new(128);
        private readonly List<Player> _validateScratch = new(128);

        // Pre-allocated TimeSpans to avoid per-tick allocations in hot paths
        private static readonly TimeSpan s_rateLimitInterval1ms = TimeSpan.FromMilliseconds(1);
        private static readonly TimeSpan s_miscSleepTarget = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan s_grenadeSleepTarget = TimeSpan.FromMilliseconds(10);
        private static readonly TimeSpan s_fastSleepTarget = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan s_interactablesSleepTarget = TimeSpan.FromMilliseconds(750);
        // Static rate limiters for loop-wide exceptions (shared across all raid instances)
        private static RateLimiter s_realtimeLoopExLimit = new(TimeSpan.FromSeconds(10));
        private static RateLimiter s_validateLoopExLimit = new(TimeSpan.FromSeconds(10));

        public bool InRaid => !_disposed;
        public IReadOnlyCollection<Player> Players => _rgtPlayers;
        public IReadOnlyCollection<IExplosiveItem> Explosives => _grenadeManager;
        public IReadOnlyCollection<IExitPoint> Exits => _exfilManager;
        public LocalPlayer LocalPlayer => _rgtPlayers?.LocalPlayer;
        public LootManager Loot => _lootManager;

        public QuestManager QuestManager { get; private set; }

        public CameraManager CameraManager { get; private set; }
        private long _cameraRetryAfter;

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
            Interlocked.Exchange(ref _lastDisposedBase, 0); // Game process exited — all addresses are invalid
            LevelSettings = 0;
            MatchingProgress = 0;
            LevelSettingsResolver.Reset();
            MatchingProgressResolver.Reset();
            Il2CppClass.ForceReset();      // <?? REQUIRED
        }

        /// <summary>
        /// Clears the stale GameWorld address guard and suppresses the next
        /// <see cref="Dispose"/> from re-recording it.
        /// Call this when the user explicitly requests a radar restart so the
        /// same (still-live) GameWorld can be re-detected by <see cref="CreateGameInstance"/>.
        /// </summary>
        public static void ClearStaleGuard()
        {
            Interlocked.Exchange(ref _suppressStaleGuard, 1);
            Interlocked.Exchange(ref _lastDisposedBase, 0);
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

            // Delay camera resolution by 20s from raid start.
            // EFT's CameraManager.Instance is not available until well after raid begins.
            _cameraRetryAfter = Environment.TickCount64 + 20_000;
        }

        /// <summary>
        /// Phase 2: Wait for raid to be fully ready, then initialize players/loot.
        /// This ensures we don't read garbage transform data from menu/loading state.
        /// </summary>
        private void WaitForRaidReady(CancellationToken ct)
        {
            Log.WriteLine("[Raid] Waiting for raid to be fully ready...");

            // Camera resolution is handled lazily by RefreshCameraManager() in FastWorker.
            // Loading times vary, so we don't block on camera here.

            Log.WriteLine("[Raid] Waiting for LocalPlayer to be fully in raid...");

            // Phase 2: Wait for LocalPlayer to be valid (RegisteredPlayers list populated)
            const int maxPlayerAttempts = 60; // 30 seconds max wait
            int attempts = 0;
            bool alreadyMidRaid = false;
            while (attempts++ < maxPlayerAttempts)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (IsLocalPlayerInRaid())
                    {
                        Log.WriteLine("[Raid] LocalPlayer confirmed in raid!");
                        // attempts == 1 means we succeeded on the very first try with no sleep,
                        // i.e. the radar was launched / restarted while already mid-raid.
                        alreadyMidRaid = attempts == 1;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (attempts % 10 == 0)
                        Log.WriteLine($"[Raid] Waiting... ({ex.Message})");
                }

                ct.WaitHandle.WaitOne(500);
            }

            if (attempts >= maxPlayerAttempts)
                Log.WriteLine("[Raid] Timeout waiting for raid confirmation, proceeding anyway...");

            // When launched or restarted mid-raid the camera is already initialised by EFT,
            // so skip the 20 s initial delay and let RefreshCameraManager attempt immediately.
            if (alreadyMidRaid)
            {
                _cameraRetryAfter = 0;
                Log.WriteLine("[Raid] Mid-raid entry detected, skipping camera init delay.");
            }

            const int maxInitAttempts = 10;
            for (int initAttempt = 1; initAttempt <= maxInitAttempts; initAttempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    InitializeGameData(ct);
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (initAttempt < maxInitAttempts)
                {
                    Log.WriteLine($"[Raid] Game data init attempt {initAttempt}/{maxInitAttempts} failed ({ex.Message}), retrying in 3s...");
                    ct.WaitHandle.WaitOne(3000);
                }
            }

            Log.WriteLine("[Raid] Waiting for real raid start...");

            if (IsInRealRaid())
                Log.WriteLine("[Raid] Raid fully active!");
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

            Log.WriteLine($"[Raid] RegisteredPlayers validated: {playerCount} player(s)");
            return true;
        }

        /// <summary>
        /// Initialize players, loot, and other game data.
        /// Only called after raid is confirmed ready.
        /// </summary>
        private void InitializeGameData(CancellationToken ct)
        {
            Log.WriteLine("[Raid] Initializing game data...");

            var rgtPlayersAddr = Memory.ReadPtr(Base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
            _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
            if (_rgtPlayers.GetPlayerCount() < 1)
                throw new InvalidOperationException("RegisteredPlayers count is less than 1.");

            _lootManager = new LootManager(Base, ct);
            _exfilManager = new ExitManager(Base, _rgtPlayers.LocalPlayer.IsPmc);
            _grenadeManager = new ExplosivesManager(Base);
            Log.WriteLine($"[WorldInteractablesManager] Calling from LocalGameWorld: 0x{Base:X}");
            _worldInteractablesManager = new WorldInteractablesManager(Base);

            Log.WriteLine("[Raid] Game data initialized successfully!");
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

                // Resolve MatchingProgressView once, then update the live stage on every tick.
                if (!MatchingProgressResolver.TryGetCached(out _))
                    MatchingProgressResolver.ResolveAsync();

                MatchingProgressResolver.TryUpdateStage();

                try
                {
                    // Phase 1: Find GameWorld (minimal init)
                    var instance = GetLocalGameWorld(ct);

                    // Reject stale GameWorld that Unity keeps alive on the post-raid menu.
                    // The same Base address means the object was not destroyed and recreated.
                    if (instance.Base == Interlocked.Read(ref _lastDisposedBase))
                        throw new InvalidOperationException("GameWorld not found");

                    // Accepted — this is a genuinely new GameWorld instance.
                    Interlocked.Exchange(ref _lastDisposedBase, 0);

                    // Assign MatchingProgress from cache (may already be resolved)
                    if (MatchingProgressResolver.TryGetCached(out var mp) && mp.IsValidVirtualAddress())
                    {
                        MatchingProgress = mp;
                        Log.WriteLine($"[IL2CPP] MatchingProgress assigned @ 0x{mp:X}");
                    }

                    // Matching phase is over — stop the stage poller and freeze the timer
                    MatchingProgressResolver.NotifyRaidStarted();

                    // Phase 2: Wait for raid to be ready, then initialize game data
                    instance.WaitForRaidReady(ct);

                    Log.WriteLine("Raid has started!");
                    return instance;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"ERROR Instantiating Game Instance: {ex.InnerException?.Message ?? ex.Message}");
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

                // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
                // OFFLINE / ONLINE detection (cheap)
                // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
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

                    Log.WriteLine($"[IL2CPP] Raid Mode: {(IsOffline ? "OFFLINE" : "ONLINE")}");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[IL2CPP] Could not detect offline mode: {ex.Message}");
                    IsOffline = false;
                }

                // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
                // LEVEL SETTINGS ¡§C non-blocking
                // ?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è?¡è
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
                        // 2) No cached value yet ¡§C schedule a background resolve.
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
                                    Log.WriteLine($"[IL2CPP] LevelSettings resolved async @ 0x{ls:X}");
                                }
                            }
                            catch (Exception ex2)
                            {
                                Log.WriteLine($"[IL2CPP] Async LevelSettings resolve failed: {ex2.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[IL2CPP] LevelSettings resolution error: {ex.Message}");
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
                Log.WriteLine("Raid has ended!");
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
                Log.WriteLine($"CRITICAL ERROR - Raid ended due to unhandled exception: {ex}");
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
                        Log.WriteLine($"[RaidCooldown] Waiting {waitMs} ms before next raid init...");
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
                    if (IsRaidActive())
                        return;
                }
                catch { }
                Thread.Sleep(10); // short delay between attempts
            }

            // Definitively over — clean up once then signal
            LevelSettings = 0;
            MatchingProgress = 0;
            LevelSettingsResolver.Reset();
            MatchingProgressResolver.Reset();
            Il2CppClass.ForceReset();
            GuardManager.ClearCache();
            throw new RaidEnded();
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

                // 3) Map transition detection ¡§C but not on every single call
                if ((_mapCheckTick++ & 0x3F) == 0) // every 64 calls
                {
                    var currentMapId = GetCurrentMapId();
                    if (!string.IsNullOrEmpty(currentMapId) &&
                        !string.IsNullOrEmpty(MapID) &&
                        !string.Equals(currentMapId, MapID, StringComparison.Ordinal))
                    {
                        Log.WriteLine($"[Raid] Map changed: '{MapID}' -> '{currentMapId}'. Marking raid as ended.");
                        return false;
                    }
                }

                return true;
            }
            catch
            {
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
                    Log.WriteLine("Not Fully in raid yet (no LocalPlayer)...");
                    return false;
                }

                ulong handsController = local.Firearm.HandsController.Item1;
                if (!Utils.IsValidVirtualAddress(handsController))
                {
                    Log.WriteLine("Not Fully in raid yet (hands controller invalid)...");
                    return false;
                }

                if (!_raidStarted)
                {
                    _raidStarted = true;

                    // Trigger player stats fetch for any player whose accountId was
                    // already seeded from a previous corpse dogtag read.
                    Task.Run(() =>
                    {
                        foreach (var player in Memory.Players)
                        {
                            if (player is null)
                                continue;
                            try
                            {
                                var cached = PlayerLookupApiClient.TryGetCached(player.ProfileID);
                                if (cached?.AccountId is string acctId)
                                    EFTProfileService.RegisterProfile(acctId);
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLine($"[Raid] EFTProfileService error for Player {player}: {ex}");
                            }
                        }
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
                Log.WriteLine("Realtime thread starting...");
                while (InRaid)
                {
                    if (Memory.IsDisposed) { Dispose(); break; }
                    if (Config.RatelimitRealtimeReads || !CameraManagerBase.EspRunning || (MemWriteFeature<Aimbot>.Instance.Enabled && Aimbot.Engaged))
                    {
                        _refreshWait.AutoWait(s_rateLimitInterval1ms, 1000);
                    }

                    ct.ThrowIfCancellationRequested();
                    RealtimeLoop(); // Realtime update loop (player positions, etc.)
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR on Realtime Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                Log.WriteLine("Realtime thread stopping...");
            }
        }

        /// <summary>
        /// Updates all Realtime Values (View Matrix, player positions, etc.)
        /// </summary>
        private void RealtimeLoop()
        {
            try
            {
                var localPlayer = LocalPlayer;

                // Single pass: collect active+alive players into pre-allocated scratch list
                _realtimeScratch.Clear();
                foreach (var p in _rgtPlayers)
                {
                    if (p.IsActive && p.IsAlive)
                        _realtimeScratch.Add(p);
                }

                if (_realtimeScratch.Count == 0)
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

                var count = _realtimeScratch.Count;
                for (int i = 0; i < count; i++)
                {
                    var p = _realtimeScratch[i];
                    try
                    {
                        p.OnRealtimeLoop(round1[i]);
                    }
                    catch (NullReferenceException nre)
                    {
                        if (p.RealtimeNreLimit.TryEnter())
                            Log.Write(AppLogLevel.Warning,
                                $"[{p.Name} @ 0x{p.Base:X}] OnRealtimeLoop NRE (transient allocation race): {nre.Message}",
                                "RealtimeLoop");
                    }
                }

                scatterMap.Execute(); // Execute scatter read
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (s_realtimeLoopExLimit.TryEnter())
                    Log.Write(AppLogLevel.Warning,
                        $"UpdatePlayers Loop FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                        "RealtimeLoop");
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
                Log.WriteLine("Misc thread starting...");
                while (InRaid)
                {
                    if (Memory.IsDisposed) { Dispose(); break; }
                    ct.ThrowIfCancellationRequested();
                    long start = Stopwatch.GetTimestamp();
                    UpdateMisc();
                    // Dynamic sleep: target 50ms total cycle time (subtract work duration)
                    var elapsed = Stopwatch.GetElapsedTime(start);
                    var remaining = s_miscSleepTarget - elapsed;
                    if (remaining > TimeSpan.Zero)
                        Thread.Sleep(remaining);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR on Misc Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                Log.WriteLine("Misc thread stopping...");
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
                    Log.WriteLine($"[Wishlist] ERROR Refreshing: {ex}");
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
                    Log.WriteLine($"[QuestManager] CRITICAL ERROR: {ex}");
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
                foreach (var player in _rgtPlayers)
                {
                    if (player.IsHostileActive)
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
                // Single pass: collect eligible players into pre-allocated scratch list
                _validateScratch.Clear();
                foreach (var p in _rgtPlayers)
                {
                    if (p.IsActive && p.IsAlive && p is not BtrOperator)
                        _validateScratch.Add(p);
                }

                if (_validateScratch.Count > 0)
                {
                    using var scatterMap = ScatterReadMap.Get();
                    var round1 = scatterMap.AddRound();
                    var round2 = scatterMap.AddRound();
                    var count = _validateScratch.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var p = _validateScratch[i];
                        try
                        {
                            p.OnValidateTransforms(round1[i], round2[i]);
                        }
                        catch (NullReferenceException nre)
                        {
                            if (p.ValidateNreLimit.TryEnter())
                                Log.Write(AppLogLevel.Warning,
                                    $"[{p.Name} @ 0x{p.Base:X}] OnValidateTransforms NRE (transient allocation race): {nre.Message}",
                                    "ValidateTransforms");
                        }
                    }
                    scatterMap.Execute(); // execute scatter read
                }
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (s_validateLoopExLimit.TryEnter())
                    Log.Write(AppLogLevel.Warning,
                        $"ValidatePlayerTransforms Loop FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}",
                        "ValidateTransforms");
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
                Log.WriteLine("Grenades thread starting...");
                while (InRaid)
                {
                    if (Memory.IsDisposed) { Dispose(); break; }
                    ct.ThrowIfCancellationRequested();
                    long start = Stopwatch.GetTimestamp();
                    _grenadeManager.Refresh();
                    var remaining = s_grenadeSleepTarget - Stopwatch.GetElapsedTime(start);
                    if (remaining > TimeSpan.Zero)
                        Thread.Sleep(remaining);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR on Grenades Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                Log.WriteLine("Grenades thread stopping...");
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
                Log.WriteLine("FastWorker thread starting...");
                while (InRaid)
                {
                    if (Memory.IsDisposed) { Dispose(); break; }
                    ct.ThrowIfCancellationRequested();
                    long start = Stopwatch.GetTimestamp();
                    RefreshCameraManager();
                    RefreshFast();
                    var remaining = s_fastSleepTarget - Stopwatch.GetElapsedTime(start);
                    if (remaining > TimeSpan.Zero)
                        Thread.Sleep(remaining);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR on FastWorker Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                Log.WriteLine("FastWorker thread stopping...");
            }
        }

        private void InteractablesWorker(CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                Log.WriteLine("Interactables thread starting...");
                while (InRaid)
                {
                    if (Memory.IsDisposed) { Dispose(); break; }
                    ct.ThrowIfCancellationRequested();
                    long start = Stopwatch.GetTimestamp();
                    RefreshWorldInteractables();
                    var remaining = s_interactablesSleepTarget - Stopwatch.GetElapsedTime(start);
                    if (remaining > TimeSpan.Zero)
                        Thread.Sleep(remaining);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL ERROR on Interactables Thread: {ex}");
                Dispose(); // Game object is in a corrupted state --> Dispose
            }
            finally
            {
                Log.WriteLine("Interactables thread stopping...");
            }
        }

        private void RefreshCameraManager()
        {
            if (CameraManager is not null)
                return;
            if (Environment.TickCount64 < _cameraRetryAfter)
                return;
            try
            {
                CameraManager = new CameraManager();
                Log.WriteLine("[CameraManager] Camera resolved!");
            }
            catch
            {
                // Back off 3s before next attempt — Instance takes time to become available after raid start
                _cameraRetryAfter = Environment.TickCount64 + 3_000;
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
                foreach (var player in _rgtPlayers)
                {
                    if (!player.IsActive || !player.IsAlive)
                        continue;

                    player.RefreshHands();
                    if (player is LocalPlayer localPlayer)
                        localPlayer.Firearm.Update();
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
                // Record this address so CreateGameInstance rejects the stale
                // GameWorld that Unity keeps alive on the post-raid menu screen.
                // Skip when the user explicitly requested a restart — the GameWorld
                // is still live and should be re-detectable.
                if (Interlocked.Exchange(ref _suppressStaleGuard, 0) == 0)
                    Interlocked.Exchange(ref _lastDisposedBase, Base);

                Log.WriteLine("[Raid] LocalGameWorld disposed — entering cooldown.");

                _raidStarted = false;

                LevelSettings = 0;
                LevelSettingsResolver.Reset();
                EftHardSettingsResolver.InvalidateCache();
                EftWeatherControllerResolver.InvalidateCache();

                Il2CppClass.ForceReset();

                // 10
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