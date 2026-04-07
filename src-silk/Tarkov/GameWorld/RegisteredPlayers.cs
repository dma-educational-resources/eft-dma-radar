using SDK;
using eft_dma_radar.Silk.Tarkov.GameWorld.Player;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Manages registered players in a raid — reads, caches, and updates player data.
    /// Mirrors WPF RegisteredPlayers structure.
    /// </summary>
    internal sealed class RegisteredPlayers : IReadOnlyCollection<Player.Player>
    {
        #region Constants

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

        // Maximum parent-chain iterations (safety guard)
        private const int MaxHierarchyIterations = 4000;

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();

        #endregion

        #region Properties

        public Player.Player? LocalPlayer { get; private set; }
        public int Count => _players.Count;

        #endregion

        #region Inner Types

        /// <summary>
        /// Pairs a <see cref="Player.Player"/> with its cached transform data so we can avoid
        /// re-walking the pointer chain on every tick.
        /// </summary>
        private sealed class PlayerEntry
        {
            public Player.Player Player { get; }

            // Cached transform state (populated once, re-validated periodically)
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public ulong IndicesAddr;
            public int TransformIndex;
            public bool TransformReady;

            // Cached rotation address
            public ulong RotationAddr;
            public bool RotationReady;

            public PlayerEntry(Player.Player player) => Player = player;
        }

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

        #region Constructor

        internal RegisteredPlayers(ulong gameWorldBase)
        {
            _gameWorldBase = gameWorldBase;
        }

        #endregion

        #region IReadOnlyCollection

        public IEnumerator<Player.Player> GetEnumerator() =>
            _players.Values.Select(e => e.Player).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        #endregion

        #region Public Methods

        /// <summary>
        /// Blocks until the local player (MainPlayer) is found.
        /// </summary>
        internal void WaitForLocalPlayer(CancellationToken ct)
        {
            Log.WriteLine("[RegisteredPlayers] Waiting for LocalPlayer...");
            const int maxAttempts = 60;
            for (int i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var mainPlayerPtr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.MainPlayer, false);
                    if (!eft_dma_radar.Silk.Misc.Utils.IsValidVirtualAddress(mainPlayerPtr))
                    {
                        if (i == 0 || i % 10 == 0)
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer ptr invalid: 0x{mainPlayerPtr:X}");
                        ct.WaitHandle.WaitOne(500);
                        continue;
                    }

                    var className = ReadClassName(mainPlayerPtr);
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer=0x{mainPlayerPtr:X} class='{className ?? "<null>"}'");

                    var entry = ReadPlayer(mainPlayerPtr, isLocal: true);
                    if (entry is not null)
                    {
                        LocalPlayer = entry.Player;
                        _players[mainPlayerPtr] = entry;
                        Log.WriteLine($"[RegisteredPlayers] LocalPlayer found: {entry.Player.Name} (class='{className ?? "<null>"}')");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] WaitForLocalPlayer attempt {i}: {ex.Message}");
                }

                ct.WaitHandle.WaitOne(500);
            }

            Log.WriteLine("[RegisteredPlayers] Timeout waiting for LocalPlayer, proceeding anyway.");
        }

        /// <summary>
        /// Refreshes the player list: discovers new players, marks gone ones inactive, updates positions/rotations.
        /// </summary>
        internal void Refresh()
        {
            ulong rgtPlayersAddr, listItemsPtr;
            int count;
            try
            {
                rgtPlayersAddr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                listItemsPtr   = Memory.ReadPtr(rgtPlayersAddr + 0x10, false);
                count          = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_list", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Refresh: failed to read RegisteredPlayers list ({ex.GetType().Name}): {ex.Message}");
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
                    $"[RegisteredPlayers] Refresh: failed to read player pointer array (count={count}, {ex.GetType().Name}): {ex.Message}");
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

        #endregion

        #region Player Read

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

                var className = ReadClassName(playerBase);
                bool isObserved = className is not (null or "ClientPlayer" or "LocalPlayer") && !isLocal;

                if (isObserved)
                {
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

                Player.Player player = isLocal
                    ? new LocalPlayer { Name = name, Type = type, IsAlive = true, IsActive = true }
                    : new Player.Player { Name = name, Type = type, IsAlive = true, IsActive = true };

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

        private static void TryInitTransform(ulong playerBase, PlayerEntry entry, bool isObserved)
        {
            try
            {
                uint bodyOffset = isObserved
                    ? Offsets.ObservedPlayerView.PlayerBody
                    : Offsets.Player._playerBody;

                var bodyPtr       = Memory.ReadPtr(playerBase + bodyOffset, false);
                var skelRootJoint = Memory.ReadPtr(bodyPtr + Offsets.PlayerBody.SkeletonRootJoint, false);
                var dizValues     = Memory.ReadPtr(skelRootJoint + Offsets.DizSkinningSkeleton._values, false);
                var arrPtr        = Memory.ReadPtr(dizValues + ListArrOffset, false);
                var boneEntryPtr  = Memory.ReadPtr(arrPtr + ListArrStartOffset, false);
                var transformInternal = Memory.ReadPtr(boneEntryPtr + 0x10, false);

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

        private static void TryInitRotation(ulong playerBase, PlayerEntry entry, bool isObserved)
        {
            try
            {
                ulong rotAddr;
                if (isObserved)
                {
                    var opc = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, false);
                    var mc  = Memory.ReadPtrChain(opc, Offsets.ObservedPlayerController.MovementController, false);
                    rotAddr = mc + 0x28;
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + 0xC0;
                }

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
                bool isObserved = IsObservedPlayer(playerBase);
                TryInitRotation(playerBase, entry, isObserved);
            }

            // --- Position (hierarchy walk) ---
            if (!entry.TransformReady)
            {
                bool isObserved = IsObservedPlayer(playerBase);
                TryInitTransform(playerBase, entry, isObserved);
                if (!entry.TransformReady) return;
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
                worldPos  = Vector3.Transform(worldPos, parent.q);
                worldPos *= parent.s;
                worldPos += parent.t;

                idx = indices[idx];
            }

            return worldPos;
        }

        #endregion

        #region Helpers

        private static string? ReadClassName(ulong playerBase)
        {
            try
            {
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

        private static PlayerType ResolvePlayerType(int side, bool isLocal, string name, ulong playerBase, bool isObserved)
        {
            if (isLocal) return PlayerType.Default;
            if (side == 4 && isObserved)
            {
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
    }
}
