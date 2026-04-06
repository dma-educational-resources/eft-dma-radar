using SDK;
using eft_dma_radar.Silk.Tarkov.Unity;
using System.Diagnostics;

namespace eft_dma_radar.Silk.Tarkov
{
    /// <summary>
    /// Minimal raid session. Reads players (position + rotation) and raid lifecycle.
    /// Phase 1 — no loot, no exits, no quests.
    /// </summary>
    internal sealed class GameSession : IDisposable
    {
        #region Fields

        private readonly ulong _base;
        private readonly CancellationToken _ct;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();
        private volatile bool _disposed;
        private Thread? _refreshThread;

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(133);
        private static readonly TimeSpan RaidEndedCheckInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastRaidEndedCheck = DateTime.MinValue;

        // UnityList<T> layout constants (matches WPF UnityList<T>)
        private const uint ListArrOffset = 0x10;
        private const uint ListArrStartOffset = 0x20;

        // TransformInternal → TransformAccess pointer offset
        private const uint TI_TransformAccessOffset = 0x90;

        // TransformAccess field offsets (matches UnityOffsets)
        private const uint TA_IndexOffset = 0x78;
        private const uint TA_HierarchyOffset = 0x70;

        // TransformHierarchy field offsets
        private const uint TH_VerticesOffset = 0x68;
        private const uint TH_IndicesOffset = 0x40;

        // GameWorld component extraction chain: GameObject → ComponentArray[0x58] → entry[0x18] → ObjectClass[0x20]
        private static readonly uint[] GameWorldChain = [0x58, 0x18, 0x20];

        // Maximum parent-chain iterations (safety guard)
        private const int MaxHierarchyIterations = 4000;

        #endregion

        #region Properties

        public string MapID { get; }
        public bool InRaid => !_disposed;
        public IReadOnlyCollection<PlayerBase> Players => _players.Values
            .Select(e => e.Player)
            .ToArray();
        public PlayerBase? LocalPlayer { get; private set; }

        #endregion

        #region Inner type

        /// <summary>
        /// Pairs a <see cref="PlayerBase"/> with its cached transform data so we can avoid
        /// re-walking the pointer chain on every tick.
        /// </summary>
        private sealed class PlayerEntry
        {
            public PlayerBase Player { get; }

            // Cached transform state (populated once, re-validated periodically)
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public ulong IndicesAddr;
            public int TransformIndex;
            public bool TransformReady;

            // Cached rotation address
            public ulong RotationAddr;
            public bool RotationReady;

            public PlayerEntry(PlayerBase player) => Player = player;
        }

        #endregion

        #region Factory

        /// <summary>
        /// Scans the GOM for a live LocalGameWorld and creates a GameSession from it.
        /// Blocks until found or throws if the game process is gone.
        /// </summary>
        public static GameSession Create(CancellationToken ct)
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
                            "[GameSession] GameWorld found but no MainPlayer yet — waiting for raid...");
                        Thread.Sleep(500);
                        continue;
                    }

                    var mapId = ReadMapID(gameWorld);
                    Log.WriteLine($"[GameSession] Found GameWorld @ 0x{gameWorld:X}, map = '{mapId}'");
                    return new GameSession(gameWorld, mapId, ct);
                }
                catch (Memory.GameNotRunningException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.WriteRateLimited(AppLogLevel.Info, "gw_search", TimeSpan.FromSeconds(5),
                        $"[GameSession] Waiting for raid... ({ex.Message})");
                    Thread.Sleep(500);
                }
            }
        }

        private GameSession(ulong gameWorldBase, string mapId, CancellationToken ct)
        {
            _base = gameWorldBase;
            MapID = mapId;
            _ct = ct;
        }

        #endregion

        #region Lifecycle

        /// <summary>Starts the background player refresh thread.</summary>
        public void Start()
        {
            WaitForLocalPlayer();
            _refreshThread = new Thread(RefreshWorker) { IsBackground = true, Name = "GameSession.Refresh" };
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
                    RefreshPlayers();
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
                        $"[GameSession] Refresh error ({ex.GetType().Name}): {ex.Message}\n{ex.StackTrace}");
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

            // Extract ClientLocalGameWorld component via: GameObject+0x58 -> ComponentArray+0x18 -> ComponentEntry+0x20
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

        private void WaitForLocalPlayer()
        {
            Log.WriteLine("[GameSession] Waiting for LocalPlayer...");
            const int maxAttempts = 60;
            for (int i = 0; i < maxAttempts; i++)
            {
                _ct.ThrowIfCancellationRequested();
                try
                {
                    var mainPlayerPtr = Memory.ReadPtr(_base + Offsets.ClientLocalGameWorld.MainPlayer, false);
                    if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(mainPlayerPtr))
                    {
                        if (i == 0 || i % 10 == 0)
                            Log.Write(AppLogLevel.Debug, $"[GameSession] MainPlayer ptr invalid: 0x{mainPlayerPtr:X}");
                        _ct.WaitHandle.WaitOne(500);
                        continue;
                    }

                    var className = ReadClassName(mainPlayerPtr);
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[GameSession] MainPlayer=0x{mainPlayerPtr:X} class='{className ?? "<null>"}'");

                    var entry = ReadPlayer(mainPlayerPtr, isLocal: true);
                    if (entry is not null)
                    {
                        LocalPlayer = entry.Player;
                        _players[mainPlayerPtr] = entry;
                        Log.WriteLine($"[GameSession] LocalPlayer found: {entry.Player.Name} (class='{className ?? "<null>"}')");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[GameSession] WaitForLocalPlayer attempt {i}: {ex.Message}");
                }

                _ct.WaitHandle.WaitOne(500);
            }

            Log.WriteLine("[GameSession] Timeout waiting for LocalPlayer, proceeding anyway.");
        }

        #endregion

        #region Player Refresh

        private void RefreshPlayers()
        {
            ulong rgtPlayersAddr, listItemsPtr;
            int count;
            try
            {
                rgtPlayersAddr = Memory.ReadPtr(_base + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                listItemsPtr   = Memory.ReadPtr(rgtPlayersAddr + 0x10, false);
                count          = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_list", TimeSpan.FromSeconds(5),
                    $"[GameSession] RefreshPlayers: failed to read RegisteredPlayers list ({ex.GetType().Name}): {ex.Message}");
                return;
            }

            if (count < 1 || count > 256) return;

            ulong[] ptrs;
            try
            {
                ptrs = Memory.ReadArray<ulong>(listItemsPtr + 0x20, count, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_ptrs", TimeSpan.FromSeconds(5),
                    $"[GameSession] RefreshPlayers: failed to read player pointer array (count={count}, {ex.GetType().Name}): {ex.Message}");
                return;
            }

            var seen = new HashSet<ulong>(count);

            foreach (var ptr in ptrs)
            {
                if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(ptr)) continue;
                seen.Add(ptr);

                if (_players.ContainsKey(ptr)) continue;

                var entry = ReadPlayer(ptr, isLocal: false);
                if (entry is not null)
                    _players.TryAdd(ptr, entry);
            }

            // Mark gone players as inactive
            foreach (var kvp in _players)
            {
                if (!seen.Contains(kvp.Key))
                    kvp.Value.Player.IsActive = false;
            }

            // Update position + rotation for every active player
            foreach (var kvp in _players)
            {
                if (!kvp.Value.Player.IsActive) continue;
                TryUpdatePlayer(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// Reads name, side, and allocates a <see cref="PlayerEntry"/> for a new player address.
        /// Returns null if the read fails or data looks invalid.
        /// </summary>
        private static PlayerEntry? ReadPlayer(ulong playerBase, bool isLocal)
        {
            try
            {
                string name;
                int sideRaw;

                // Detect class: ObservedPlayerView exposes NickName and Side directly at fixed offsets;
                // ClientPlayer/LocalPlayer must go via Profile→Info.
                // If the class name cannot be read (null), fall back based on isLocal:
                // the MainPlayer pointer is always a ClientPlayer/LocalPlayer type.
                var className = ReadClassName(playerBase);
                bool isObserved = className is not (null or "ClientPlayer" or "LocalPlayer") && !isLocal;

                if (isObserved)
                {
                    // ObservedPlayerView: NickName @ 0xB8, Side @ 0x94
                    var nicknamePtr = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.NickName, false);
                    name = Memory.ReadUnityString(nicknamePtr, 64, false);
                    sideRaw = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Side, false);
                }
                else
                {
                    var profilePtr = Memory.ReadPtr(playerBase + Offsets.Player.Profile, false);
                    var infoPtr = Memory.ReadPtr(profilePtr + Offsets.Profile.Info, false);
                    var nicknamePtr = Memory.ReadPtr(infoPtr + Offsets.PlayerInfo.Nickname, false);
                    name = Memory.ReadUnityString(nicknamePtr, 64, false);
                    sideRaw = Memory.ReadValue<int>(infoPtr + Offsets.PlayerInfo.Side, false);
                }

                if (string.IsNullOrWhiteSpace(name)) return null;

                var type = ResolvePlayerType(sideRaw, isLocal, name, playerBase, isObserved);
                var player = new PlayerBase
                {
                    Name = name,
                    Type = type,
                    IsLocalPlayer = isLocal,
                    IsAlive = true,
                    IsActive = true
                };

                var entry = new PlayerEntry(player) { };
                // Pre-warm the transform cache so the first draw tick has a position
                TryInitTransform(playerBase, entry, isObserved);
                TryInitRotation(playerBase, entry, isObserved);
                return entry;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[ReadPlayer] 0x{playerBase:X} isLocal={isLocal} ({ex.GetType().Name}): {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Position / Rotation

        /// <summary>
        /// Walk the pointer chain once to find TransformInternal, then cache the transform addresses.
        /// ObservedPlayer uses PlayerBody @ 0xD8; ClientPlayer/LocalPlayer uses _playerBody @ 0x190.
        /// Chain: playerBody → SkeletonRootJoint → _values.arr[HumanBase] → TransformInternal
        /// </summary>
        private static void TryInitTransform(ulong playerBase, PlayerEntry entry, bool isObserved)
        {
            try
            {
                uint bodyOffset = isObserved
                    ? Offsets.ObservedPlayerView.PlayerBody   // 0xD8
                    : Offsets.Player._playerBody;             // 0x190

                var bodyPtr       = Memory.ReadPtr(playerBase + bodyOffset, false);
                var skelRootJoint = Memory.ReadPtr(bodyPtr + Offsets.PlayerBody.SkeletonRootJoint, false);
                var dizValues     = Memory.ReadPtr(skelRootJoint + Offsets.DizSkinningSkeleton._values, false);
                // UnityList array: +0x10 = array ptr, +0x20 = first element (HumanBase = index 0 → offset 0)
                var arrPtr        = Memory.ReadPtr(dizValues + ListArrOffset, false);
                var boneEntryPtr  = Memory.ReadPtr(arrPtr + ListArrStartOffset, false); // [HumanBase=0] * 8 = 0
                var transformInternal = Memory.ReadPtr(boneEntryPtr + 0x10, false);

                // TransformInternal → TransformAccess pointer, then read Index/Hierarchy from that
                var taPtr      = Memory.ReadPtr(transformInternal + TI_TransformAccessOffset, false);
                var taIndex    = Memory.ReadValue<int>(taPtr + TA_IndexOffset, false);
                var taHierarchy = Memory.ReadPtr(taPtr + TA_HierarchyOffset, false);

                if (taIndex < 0 || taIndex > 128_000) return;
                if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(taHierarchy)) return;

                var verticesAddr = Memory.ReadPtr(taHierarchy + TH_VerticesOffset, false);
                var indicesAddr  = Memory.ReadPtr(taHierarchy + TH_IndicesOffset, false);

                if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(verticesAddr)) return;
                if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(indicesAddr)) return;

                entry.TransformInternal = transformInternal;
                entry.TransformIndex    = taIndex;
                entry.VerticesAddr      = verticesAddr;
                entry.IndicesAddr       = indicesAddr;
                entry.TransformReady    = true;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[TryInitTransform] 0x{playerBase:X} ({ex.GetType().Name}): {ex.Message}");
                entry.TransformReady = false;
            }
        }

        /// <summary>
        /// Cache the rotation address.
        /// For ObservedPlayer: ObservedPlayerController → MovementController chain → StateContext → Rotation (+0x20).
        /// For ClientPlayer/LocalPlayer: MovementContext → _rotation (+0xC8).
        /// </summary>
        private static void TryInitRotation(ulong playerBase, PlayerEntry entry, bool isObserved)
        {
            try
            {
                ulong rotAddr;
                if (isObserved)
                {
                    // ObservedPlayerView.ObservedPlayerController (+0x28) → MovementController[0xD8,0x98] → Rotation (+0x28)
                    var opc = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, false);
                    var mc  = Memory.ReadPtrChain(opc, Offsets.ObservedPlayerController.MovementController, false);
                    rotAddr = mc + 0x28; // ObservedMovementController.Rotation (confirmed 0x28 from il2cpp_offsets.json)
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + 0xC0; // MovementContext._rotation (confirmed 0xC0 from il2cpp_offsets.json)
                }

                // Sanity-check: read once and verify it looks like a valid rotation
                var rot = Memory.ReadValue<Vector2>(rotAddr, false);
                if (!float.IsFinite(rot.X) || !float.IsFinite(rot.Y)) return;
                if (MathF.Abs(rot.X) > 360f || MathF.Abs(rot.Y) > 90f) return;

                entry.RotationAddr  = rotAddr;
                entry.RotationReady = true;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[TryInitRotation] 0x{playerBase:X} ({ex.GetType().Name}): {ex.Message}");
                entry.RotationReady = false;
            }
        }

        /// <summary>
        /// Per-tick update: reads rotation
        /// Falls back gracefully — if position fails we at least still have a stale position rather than zero.
        /// </summary>
        private static void TryUpdatePlayer(ulong playerBase, PlayerEntry entry)
        {
            // --- Rotation (fast path: single value read) ---
            if (entry.RotationReady)
            {
                try
                {
                    var rot = Memory.ReadValue<Vector2>(entry.RotationAddr, false);
                    if (float.IsFinite(rot.X) && MathF.Abs(rot.X) <= 360f)
                        entry.Player.RotationYaw = rot.X;
                }
                catch (Exception ex)
                {
                    Log.Write(AppLogLevel.Debug, $"[TryUpdatePlayer] rotation 0x{playerBase:X} ({ex.GetType().Name}): {ex.Message}");
                    entry.RotationReady = false;
                }
            }
            else
            {
                // Lazy re-init
                bool isObserved = IsObservedPlayer(playerBase);
                TryInitRotation(playerBase, entry, isObserved);
            }

            // --- Position (hierarchy walk) ---
            if (!entry.TransformReady)
            {
                bool isObserved = IsObservedPlayer(playerBase);
                TryInitTransform(playerBase, entry, isObserved);
                if (!entry.TransformReady) return; // No transform available yet — keep stale position
            }

            try
            {
                var pos = ComputeWorldPosition(entry);
                if (float.IsFinite(pos.X) && float.IsFinite(pos.Y) && float.IsFinite(pos.Z))
                    entry.Player.Position = pos;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[TryUpdatePlayer] position 0x{playerBase:X} ({ex.GetType().Name}): {ex.Message}");
                entry.TransformReady = false;
            }
        }

        /// <summary>
        /// Reads the full vertices array and walks the parent-index chain to accumulate world position.
        /// This matches the Lone radar UnityTransform.UpdatePosition() algorithm.
        /// </summary>
        private static Vector3 ComputeWorldPosition(PlayerEntry entry)
        {
            int count = entry.TransformIndex + 1;
            var vertices = Memory.ReadArray<TrsX>(entry.VerticesAddr, count, false);
            var indices  = Memory.ReadArray<int>(entry.IndicesAddr, count, false);

            var worldPos = vertices[entry.TransformIndex].t;
            int idx = indices[entry.TransformIndex];
            int iterations = 0;

            while (idx >= 0)
            {
                if (iterations++ > MaxHierarchyIterations)
                    throw new InvalidOperationException("Hierarchy walk exceeded max iterations.");

                var parent = vertices[idx];
                worldPos  = Vector3.Transform(worldPos, parent.q);  // rotate by quaternion
                worldPos *= parent.s;                                // component-wise scale
                worldPos += parent.t;                                // translate

                idx = indices[idx];
            }

            return worldPos;
        }

        /// <summary>
        /// Reads the IL2CPP class name to distinguish ObservedPlayerView from ClientPlayer/LocalPlayer.
        /// Does not throw.
        /// </summary>
        private static string? ReadClassName(ulong playerBase)
        {
            try
            {
                // ObjectClass: MonoBehaviour at +0x28, then type name ptr chain
                // Same approach as Lone radar: ObjectClass.ReadName(playerBase, 64)
                // We use the WPF IL2CPP helper via qualified name
                return SilkObjectClass.ReadName(playerBase, 64);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsObservedPlayer(ulong playerBase)
        {
            var name = ReadClassName(playerBase);
            return name is not ("ClientPlayer" or "LocalPlayer");
        }

        private void CheckRaidEnded()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRaidEndedCheck) < RaidEndedCheckInterval) return;
            _lastRaidEndedCheck = now;
            try { Memory.ThrowIfNotInGame(); }
            catch (Memory.GameNotRunningException) { _disposed = true; }
        }

        private static PlayerType ResolvePlayerType(int side, bool isLocal, string name, ulong playerBase, bool isObserved)
        {
            // EFT sides: 1 = USEC, 2 = BEAR, 4 = Savage (scav)
            if (isLocal) return PlayerType.Default;
            if (side == 4 && isObserved)
            {
                // Distinguish PMC-scav (PScav, IsAI=false) from AI scav (IsAI=true)
                try
                {
                    var isAI = Memory.ReadValue<bool>(playerBase + 0xA0, false);
                    return isAI ? PlayerType.AIScav : PlayerType.PScav;
                }
                catch { return PlayerType.AIScav; }
            }
            return side switch
            {
                1 => PlayerType.USEC,
                2 => PlayerType.BEAR,
                4 => PlayerType.AIScav,
                _ => PlayerType.Default
            };
        }

        #endregion

        #region Inline Structs

        /// <summary>
        /// TRS element in a Unity transform hierarchy vertices array.
        /// Layout: t(Vector3) + pad(float) + q(Quaternion) + s(Vector3) + pad(float) = 48 bytes
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct TrsX
        {
            public readonly Vector3 t;    // translation (12 bytes)
            public readonly float _pad0;  // padding (4 bytes)
            public readonly Quaternion q; // rotation (16 bytes)
            public readonly Vector3 s;    // scale (12 bytes)
            public readonly float _pad1;  // padding (4 bytes)
        }

        #endregion
    }
}
