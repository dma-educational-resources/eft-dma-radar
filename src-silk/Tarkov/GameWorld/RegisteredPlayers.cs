using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Manages registered players in a raid — reads, caches, and updates player data.
    /// Currently supports local player tracking only.
    /// <para>
    /// Inspired by Lone's scatter-based refresh pattern:
    /// <list type="bullet">
    ///   <item>Registration refresh (slower): reads player list, discovers/removes players, updates lifecycle.</item>
    ///   <item>Realtime refresh (fast): scatter-batched position + rotation for all active players — single DMA round-trip.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class RegisteredPlayers : IReadOnlyCollection<Player.Player>
    {
        #region Constants

        // UnityList<T> layout constants
        private const uint ListArrOffset = 0x10;
        private const uint ListArrStartOffset = 0x20;

        // TransformAccess field offsets read directly from TransformInternal
        // Verified against il2cpp_offsets.json
        private const uint TA_IndexOffset = 0x78;
        private const uint TA_HierarchyOffset = 0x70;

        // TransformHierarchy field offsets
        private const uint TH_VerticesOffset = 0x68;
        private const uint TH_IndicesOffset = 0x40;

        // Rotation offsets (verified against il2cpp_offsets.json)
        // ObservedMovementController._Rotation = 0x28 (40 decimal)
        private const uint ObservedRotationOffset = 0x28;
        // MovementContext._rotation = 0xC0 (192 decimal)
        private const uint ClientRotationOffset = 0xC0;

        // Maximum parent-chain iterations (safety guard)
        private const int MaxHierarchyIterations = 4000;

        // Maximum valid player count from the registered players list
        private const int MaxPlayerCount = 256;

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();
        private HashSet<ulong> _seenSet = new(MaxPlayerCount);

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
            public readonly ulong Base;
            public readonly Player.Player Player;
            public readonly bool IsObserved;

            // Cached transform state (populated once, re-validated periodically)
            public ulong TransformInternal;
            public ulong VerticesAddr;
            public int TransformIndex;
            public bool TransformReady;

            // Indices never change for the life of the transform — cache once
            public int[]? CachedIndices;

            // Cached rotation address
            public ulong RotationAddr;
            public bool RotationReady;

            // Error tracking for realtime loop
            public bool HasError;

            public PlayerEntry(ulong playerBase, Player.Player player, bool isObserved)
            {
                Base = playerBase;
                Player = player;
                IsObserved = isObserved;
            }
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
            _players.Values.Select(static e => e.Player).GetEnumerator();

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
                    if (!mainPlayerPtr.IsValidVirtualAddress())
                    {
                        if (i == 0 || i % 10 == 0)
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer ptr invalid: 0x{mainPlayerPtr:X}");
                        ct.WaitHandle.WaitOne(500);
                        continue;
                    }

                    var className = ReadClassName(mainPlayerPtr);
                    if (i == 0 || i % 10 == 0)
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] MainPlayer=0x{mainPlayerPtr:X} class='{className ?? "<null>"}'");

                    var entry = CreatePlayerEntry(mainPlayerPtr, isLocal: true);
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
        /// Registration refresh: reads the player list, discovers new players, removes gone ones.
        /// Called from the slower registration worker thread.
        /// </summary>
        internal void RefreshRegistration()
        {
            ulong rgtPlayersAddr, listItemsPtr;
            int count;

            try
            {
                rgtPlayersAddr = Memory.ReadPtr(_gameWorldBase + Offsets.ClientLocalGameWorld.RegisteredPlayers, false);
                listItemsPtr = Memory.ReadPtr(rgtPlayersAddr + ListArrOffset, false);
                count = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_list", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Failed to read player list: {ex.Message}");
                return;
            }

            if (count < 1 || count > MaxPlayerCount)
                return;

            ulong[] ptrs;
            try
            {
                ptrs = Memory.ReadArray<ulong>(listItemsPtr + ListArrStartOffset, count, false);
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, "rp_ptrs", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] Failed to read pointer array (count={count}): {ex.Message}");
                return;
            }

            // Reuse the HashSet across calls to avoid per-tick allocation
            var seen = _seenSet;
            seen.Clear();
            seen.EnsureCapacity(count);

            // Discover new players
            foreach (var ptr in ptrs)
            {
                if (!ptr.IsValidVirtualAddress())
                    continue;

                seen.Add(ptr);

                if (_players.ContainsKey(ptr))
                    continue;

                var entry = CreatePlayerEntry(ptr, isLocal: false);
                if (entry is not null)
                    _players.TryAdd(ptr, entry);
            }

            // Update existing players — mark active/inactive based on registration
            UpdateExistingPlayers(seen);
        }

        /// <summary>
        /// Scatter-batched realtime update: reads position + rotation for ALL active players
        /// in a single DMA round-trip. Called from the fast realtime worker thread.
        /// </summary>
        internal void UpdateRealtimeData()
        {
            if (_players.IsEmpty)
                return;

            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);

            // Prepare all reads
            foreach (var kvp in _players)
            {
                var entry = kvp.Value;
                if (!entry.Player.IsActive)
                    continue;

                OnRealtimeLoop(scatter, entry);
            }

            // Execute single DMA round-trip — fires all Completed callbacks
            scatter.Execute();
        }

        #endregion

        #region Player Lifecycle

        /// <summary>
        /// Updates existing player states based on the current registered set.
        /// Uses scatter-batched reads for lifecycle checks (Lone's OnRegRefresh pattern).
        /// </summary>
        private void UpdateExistingPlayers(HashSet<ulong> registered)
        {
            List<ulong>? toRemove = null;

            foreach (var kvp in _players)
            {
                var entry = kvp.Value;

                if (registered.Contains(kvp.Key))
                {
                    // Player still registered — mark active
                    entry.Player.IsActive = true;
                    entry.Player.IsAlive = true;

                    // Re-init transform/rotation if they were invalidated
                    if (!entry.TransformReady)
                        TryInitTransform(entry.Base, entry);
                    if (!entry.RotationReady)
                        TryInitRotation(entry.Base, entry);
                }
                else
                {
                    // Player no longer in the registered list — mark inactive and queue for removal
                    entry.Player.IsActive = false;
                    entry.Player.IsAlive = false;
                    (toRemove ??= []).Add(kvp.Key);
                }
            }

            if (toRemove is not null)
            {
                foreach (var key in toRemove)
                    _players.TryRemove(key, out _);
            }
        }

        #endregion

        #region Realtime Loop (Scatter)

        /// <summary>
        /// Prepares scatter reads for a single player's position + rotation.
        /// Results are processed via the <see cref="VmmScatter.Completed"/> event callback.
        /// </summary>
        private static void OnRealtimeLoop(VmmScatter scatter, PlayerEntry entry)
        {
            // Prepare rotation read
            if (entry.RotationReady)
            {
                scatter.PrepareReadValue<Vector2>(entry.RotationAddr);
            }

            // Prepare position read (vertices array for hierarchy walk)
            if (entry.TransformReady)
            {
                int vertexCount = entry.TransformIndex + 1;
                scatter.PrepareReadArray<TrsX>(entry.VerticesAddr, vertexCount);
            }

            // Register callback — fires after scatter.Execute()
            scatter.Completed += (_, s) =>
            {
                bool rotOk = true;
                bool posOk = true;

                // --- Rotation ---
                if (entry.RotationReady)
                {
                    if (s.ReadValue<Vector2>(entry.RotationAddr, out var rot))
                    {
                        rotOk = SetRotation(entry, rot);
                    }
                    else
                    {
                        rotOk = false;
                    }
                }

                // --- Position ---
                if (entry.TransformReady)
                {
                    int vertexCount = entry.TransformIndex + 1;
                    var vertices = s.ReadArray<TrsX>(entry.VerticesAddr, vertexCount);
                    if (vertices is not null)
                    {
                        posOk = ComputeAndSetPosition(entry, vertices);
                    }
                    else
                    {
                        posOk = false;
                    }
                }

                entry.HasError = !rotOk || !posOk;
            };
        }

        /// <summary>
        /// Validates and applies a rotation reading.
        /// </summary>
        private static bool SetRotation(PlayerEntry entry, Vector2 rotation)
        {
            if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y))
                return false;

            float x = rotation.X % 360f;
            if (x < 0f) x += 360f;
            if (x > 360f || MathF.Abs(rotation.Y) > 90f)
                return false;

            entry.Player.RotationYaw = x;
            return true;
        }

        /// <summary>
        /// Computes the world position from a pre-read vertices array and applies it.
        /// </summary>
        private static bool ComputeAndSetPosition(PlayerEntry entry, TrsX[] vertices)
        {
            try
            {
                var indices = entry.CachedIndices!;
                var worldPos = vertices[entry.TransformIndex].t;
                int idx = indices[entry.TransformIndex];
                int iterations = 0;

                while (idx >= 0)
                {
                    if (iterations++ > MaxHierarchyIterations)
                        return false;

                    var parent = vertices[idx];
                    worldPos = Vector3.Transform(worldPos, parent.q);
                    worldPos *= parent.s;
                    worldPos += parent.t;

                    idx = indices[idx];
                }

                if (float.IsFinite(worldPos.X) && float.IsFinite(worldPos.Y) && float.IsFinite(worldPos.Z))
                {
                    entry.Player.Position = worldPos;
                    return true;
                }

                return false;
            }
            catch
            {
                entry.TransformReady = false;
                return false;
            }
        }

        #endregion

        #region Transform Validation (Scatter)

        /// <summary>
        /// Validates that cached transform addresses are still correct.
        /// Uses a two-round scatter pattern (Lone's ValidatePlayerTransforms).
        /// Round 1: read Hierarchy ptr from TransformInternal.
        /// Round 2: read VerticesAddr from Hierarchy — compare with cached value.
        /// </summary>
        internal void ValidateTransforms()
        {
            var activePlayers = _players.Values.Where(e => e.Player.IsActive && e.TransformReady).ToArray();
            if (activePlayers.Length == 0)
                return;

            using var round1 = Memory.GetScatter(VmmFlags.NOCACHE);
            using var round2 = Memory.GetScatter(VmmFlags.NOCACHE);

            foreach (var entry in activePlayers)
            {
                round1.PrepareReadPtr(entry.TransformInternal + TA_HierarchyOffset);
                round1.Completed += (_, r1) =>
                {
                    if (!r1.ReadPtr(entry.TransformInternal + TA_HierarchyOffset, out var hierarchy))
                        return;

                    round2.PrepareReadPtr(hierarchy + TH_VerticesOffset);
                    round2.Completed += (_, r2) =>
                    {
                        if (!r2.ReadPtr(hierarchy + TH_VerticesOffset, out var verticesPtr))
                            return;

                        if ((ulong)verticesPtr != entry.VerticesAddr)
                        {
                            Log.WriteLine($"[RegisteredPlayers] Transform changed for '{entry.Player.Name}' — re-initializing");
                            entry.TransformReady = false;
                            TryInitTransform(entry.Base, entry);
                        }
                    };
                };
            }

            round1.Execute();
            round2.Execute();
        }

        #endregion

        #region Player Discovery

        /// <summary>
        /// Reads name, side, and allocates a <see cref="PlayerEntry"/> for a new player address.
        /// Returns null if the read fails or data looks invalid.
        /// </summary>
        private static PlayerEntry? CreatePlayerEntry(ulong playerBase, bool isLocal)
        {
            try
            {
                var className = ReadClassName(playerBase);
                bool isObserved = !isLocal && className is not (null or "ClientPlayer" or "LocalPlayer");

                string name;
                int sideRaw;

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

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                var type = ResolvePlayerType(sideRaw, isLocal, playerBase, isObserved);

                Player.Player player = isLocal
                    ? new LocalPlayer { Name = name, Type = type, IsAlive = true, IsActive = true }
                    : new Player.Player { Name = name, Type = type, IsAlive = true, IsActive = true };

                var entry = new PlayerEntry(playerBase, player, isObserved);

                // Pre-warm caches so the first draw tick has position/rotation
                TryInitTransform(playerBase, entry);
                TryInitRotation(playerBase, entry);

                return entry;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] CreatePlayerEntry 0x{playerBase:X} isLocal={isLocal}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Transform / Rotation Init

        private static void TryInitTransform(ulong playerBase, PlayerEntry entry)
        {
            try
            {
                uint bodyOffset = entry.IsObserved
                    ? Offsets.ObservedPlayerView.PlayerBody
                    : Offsets.Player._playerBody;

                // Walk pointer chain: PlayerBody → SkeletonRootJoint → _values → arr → bone[0] → TransformInternal
                var bodyPtr = Memory.ReadPtr(playerBase + bodyOffset, false);
                var skelRootJoint = Memory.ReadPtr(bodyPtr + Offsets.PlayerBody.SkeletonRootJoint, false);
                var dizValues = Memory.ReadPtr(skelRootJoint + Offsets.DizSkinningSkeleton._values, false);
                var arrPtr = Memory.ReadPtr(dizValues + ListArrOffset, false);
                var boneEntryPtr = Memory.ReadPtr(arrPtr + ListArrStartOffset, false);
                var transformInternal = Memory.ReadPtr(boneEntryPtr + 0x10, false);

                // TransformAccess fields are embedded directly in TransformInternal
                var taIndex = Memory.ReadValue<int>(transformInternal + TA_IndexOffset, false);
                var taHierarchy = Memory.ReadPtr(transformInternal + TA_HierarchyOffset, false);

                if (taIndex < 0 || taIndex > 128_000)
                    return;
                if (!taHierarchy.IsValidVirtualAddress())
                    return;

                var verticesAddr = Memory.ReadPtr(taHierarchy + TH_VerticesOffset, false);
                var indicesAddr = Memory.ReadPtr(taHierarchy + TH_IndicesOffset, false);

                if (!verticesAddr.IsValidVirtualAddress())
                    return;
                if (!indicesAddr.IsValidVirtualAddress())
                    return;

                // Cache indices once — they never change for the life of the transform
                int count = taIndex + 1;
                var indices = Memory.ReadArray<int>(indicesAddr, count, false);

                entry.TransformInternal = transformInternal;
                entry.TransformIndex = taIndex;
                entry.VerticesAddr = verticesAddr;
                entry.CachedIndices = indices;
                entry.TransformReady = true;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitTransform 0x{playerBase:X}: {ex.Message}");
                entry.TransformReady = false;
            }
        }

        private static void TryInitRotation(ulong playerBase, PlayerEntry entry)
        {
            try
            {
                ulong rotAddr;
                if (entry.IsObserved)
                {
                    var opc = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, false);
                    var mc = Memory.ReadPtrChain(opc, Offsets.ObservedPlayerController.MovementController, false);
                    rotAddr = mc + ObservedRotationOffset;
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + ClientRotationOffset;
                }

                // Validate rotation is sane before caching
                var rot = Memory.ReadValue<Vector2>(rotAddr, false);
                if (!float.IsFinite(rot.X) || !float.IsFinite(rot.Y))
                    return;
                if (MathF.Abs(rot.X) > 360f || MathF.Abs(rot.Y) > 90f)
                    return;

                entry.RotationAddr = rotAddr;
                entry.RotationReady = true;
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] TryInitRotation 0x{playerBase:X}: {ex.Message}");
                entry.RotationReady = false;
            }
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

        private static PlayerType ResolvePlayerType(int side, bool isLocal, ulong playerBase, bool isObserved)
        {
            if (isLocal)
                return PlayerType.Default;

            if (side == 4 && isObserved)
            {
                try
                {
                    var isAI = Memory.ReadValue<bool>(playerBase + Offsets.ObservedPlayerView.IsAI, false);
                    return isAI ? PlayerType.AIScav : PlayerType.PScav;
                }
                catch
                {
                    return PlayerType.AIScav;
                }
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
