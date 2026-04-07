using System.Collections.Frozen;
using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.Unity;
using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

using static eft_dma_radar.Silk.Tarkov.Unity.UnitySDK;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Manages registered players in a raid — reads, caches, and updates player data.
    /// Supports local player, observed players (PMC, PScav, AI), and voice-based AI identification.
    /// <para>
    /// Uses a two-tier refresh model:
    /// <list type="bullet">
    ///   <item>Registration refresh (slower): reads player list, discovers/removes players, updates lifecycle.</item>
    ///   <item>Realtime refresh (fast): scatter-batched position + rotation for all active players — single DMA round-trip.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class RegisteredPlayers : IReadOnlyCollection<Player.Player>
    {
        #region Constants

        // Maximum parent-chain iterations (safety guard)
        private const int MaxHierarchyIterations = 4000;

        // Maximum valid player count from the registered players list
        private const int MaxPlayerCount = 256;

        // Spawn-group proximity threshold (squared distance, meters²)
        private const float SpawnGroupDistanceSqr = 25f; // 5m radius

        #endregion

        #region Fields

        private readonly ulong _gameWorldBase;
        private readonly string _mapId;
        private readonly ConcurrentDictionary<ulong, PlayerEntry> _players = new();
        private HashSet<ulong> _seenSet = new(MaxPlayerCount);

        // Spawn-group tracking (position-proximity-based)
        private readonly List<SpawnGroupEntry> _spawnGroups = [];
        private int _nextSpawnGroupId = 1;

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

        internal RegisteredPlayers(ulong gameWorldBase, string mapId)
        {
            _gameWorldBase = gameWorldBase;
            _mapId = mapId;
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
                listItemsPtr = Memory.ReadPtr(rgtPlayersAddr + UnityList.ArrOffset, false);
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
                ptrs = Memory.ReadArray<ulong>(listItemsPtr + UnityList.ArrStartOffset, count, false);
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
        /// Uses scatter-batched reads for lifecycle checks.
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
        /// Uses a two-round scatter pattern for validation.
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
                round1.PrepareReadPtr(entry.TransformInternal + TransformAccess.HierarchyOffset);
                round1.Completed += (_, r1) =>
                {
                    if (!r1.ReadPtr(entry.TransformInternal + TransformAccess.HierarchyOffset, out var hierarchy))
                        return;

                    round2.PrepareReadPtr(hierarchy + TransformHierarchy.VerticesOffset);
                    round2.Completed += (_, r2) =>
                    {
                        if (!r2.ReadPtr(hierarchy + TransformHierarchy.VerticesOffset, out var verticesPtr))
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
        /// For observed AI players, reads voice line for boss/raider/scav classification.
        /// Returns null if the read fails or data looks invalid.
        /// </summary>
        private PlayerEntry? CreatePlayerEntry(ulong playerBase, bool isLocal)
        {
            try
            {
                var className = ReadClassName(playerBase);
                bool isObserved = !isLocal && className is not (null or "ClientPlayer" or "LocalPlayer");

                string name;
                int sideRaw;
                PlayerType type;

                if (isObserved)
                {
                    sideRaw = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Side, false);
                    bool isScav = sideRaw == 4; // EPlayerSide.Savage

                    if (isScav)
                    {
                        var isAI = Memory.ReadValue<bool>(playerBase + Offsets.ObservedPlayerView.IsAI, false);
                        if (isAI)
                        {
                            // AI scav — identify by voice line
                            var voicePtr = Memory.ReadPtr(playerBase + Offsets.ObservedPlayerView.Voice, false);
                            var voice = Memory.ReadUnityString(voicePtr, 64, false);
                            var role = GetInitialAIRole(voice);
                            name = role.Name;
                            type = role.Type;
                        }
                        else
                        {
                            // Player scav
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = $"PScav{id}";
                            type = PlayerType.PScav;
                        }
                    }
                    else
                    {
                        // PMC (USEC/BEAR)
                        var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                        var side = sideRaw == 1 ? "Usec" : "Bear";
                        name = $"{side}{id}";
                        type = sideRaw == 1 ? PlayerType.USEC : PlayerType.BEAR;
                    }
                }
                else
                {
                    // Local / Client player — read from profile
                    var profilePtr = Memory.ReadPtr(playerBase + Offsets.Player.Profile, false);
                    var infoPtr = Memory.ReadPtr(profilePtr + Offsets.Profile.Info, false);
                    var nicknamePtr = Memory.ReadPtr(infoPtr + Offsets.PlayerInfo.Nickname, false);
                    name = Memory.ReadUnityString(nicknamePtr, 64, false);
                    sideRaw = Memory.ReadValue<int>(infoPtr + Offsets.PlayerInfo.Side, false);
                    type = isLocal ? PlayerType.Default : ResolveClientPlayerType(sideRaw);
                }

                if (string.IsNullOrWhiteSpace(name))
                    return null;

                Player.Player player = isLocal
                    ? new LocalPlayer { Name = name, Type = type, IsAlive = true, IsActive = true }
                    : new Player.Player { Name = name, Type = type, IsAlive = true, IsActive = true };

                var entry = new PlayerEntry(playerBase, player, isObserved);

                // Pre-warm caches so the first draw tick has position/rotation
                TryInitTransform(playerBase, entry);
                TryInitRotation(playerBase, entry);

                // Assign spawn-group based on initial position proximity (for human players)
                if (!isLocal && player.IsHuman && entry.TransformReady)
                    player.SpawnGroupID = GetOrAssignSpawnGroup(player.Position);

                Log.WriteLine($"[RegisteredPlayers] Discovered player: {player} @ 0x{playerBase:X} (class='{className}')");

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
                var arrPtr = Memory.ReadPtr(dizValues + UnityList.ArrOffset, false);
                var boneEntryPtr = Memory.ReadPtr(arrPtr + UnityList.ArrStartOffset, false);
                var transformInternal = Memory.ReadPtr(boneEntryPtr + 0x10, false);

                // TransformAccess fields are embedded directly in TransformInternal
                var taIndex = Memory.ReadValue<int>(transformInternal + TransformAccess.IndexOffset, false);
                var taHierarchy = Memory.ReadPtr(transformInternal + TransformAccess.HierarchyOffset, false);

                if (taIndex < 0 || taIndex > 128_000)
                    return;
                if (!taHierarchy.IsValidVirtualAddress())
                    return;

                var verticesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.VerticesOffset, false);
                var indicesAddr = Memory.ReadPtr(taHierarchy + TransformHierarchy.IndicesOffset, false);

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
                    rotAddr = mc + GameSDK.Rotation.Observed;
                }
                else
                {
                    var movCtx = Memory.ReadPtr(playerBase + Offsets.Player.MovementContext, false);
                    rotAddr = movCtx + GameSDK.Rotation.Client;
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

        private static PlayerType ResolveClientPlayerType(int side)
        {
            return side switch
            {
                1 => PlayerType.USEC,
                2 => PlayerType.BEAR,
                4 => PlayerType.AIScav,
                _ => PlayerType.Default
            };
        }

        #endregion

        #region Spawn Group Assignment

        /// <summary>
        /// Tracks a spawn position associated with a group ID.
        /// </summary>
        private sealed class SpawnGroupEntry
        {
            public int GroupId;
            public Vector3 SpawnPosition;
        }

        /// <summary>
        /// Assigns a spawn-group ID based on position proximity.
        /// Players spawning within <see cref="SpawnGroupDistanceSqr"/> of each other
        /// are placed in the same group.
        /// </summary>
        private int GetOrAssignSpawnGroup(Vector3 spawnPos)
        {
            // Check for zero/invalid spawn positions
            if (spawnPos == Vector3.Zero)
                return -1;

            foreach (var group in _spawnGroups)
            {
                if (Vector3.DistanceSquared(group.SpawnPosition, spawnPos) <= SpawnGroupDistanceSqr)
                    return group.GroupId;
            }

            int newId = _nextSpawnGroupId++;
            _spawnGroups.Add(new SpawnGroupEntry { GroupId = newId, SpawnPosition = spawnPos });
            return newId;
        }

        #endregion

        #region AI Role Identification

        /// <summary>
        /// AI role determined by voice line — contains a display name and player type.
        /// </summary>
        private readonly record struct AIRole(string Name, PlayerType Type);

        /// <summary>
        /// Known voice lines mapped to AI roles. Checked first before fallback pattern matching.
        /// </summary>
        private static readonly FrozenDictionary<string, AIRole> _aiRolesByVoice = new Dictionary<string, AIRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["BossSanitar"] = new("Sanitar", PlayerType.AIBoss),
            ["BossBully"] = new("Reshala", PlayerType.AIBoss),
            ["BossGluhar"] = new("Gluhar", PlayerType.AIBoss),
            ["SectantPriest"] = new("Priest", PlayerType.AIBoss),
            ["SectantWarrior"] = new("Cultist", PlayerType.AIRaider),
            ["BossKilla"] = new("Killa", PlayerType.AIBoss),
            ["BossTagilla"] = new("Tagilla", PlayerType.AIBoss),
            ["Boss_Partizan"] = new("Partisan", PlayerType.AIBoss),
            ["BossBigPipe"] = new("Big Pipe", PlayerType.AIBoss),
            ["BossBirdEye"] = new("Birdeye", PlayerType.AIBoss),
            ["BossKnight"] = new("Knight", PlayerType.AIBoss),
            ["Arena_Guard_1"] = new("Arena Guard", PlayerType.AIScav),
            ["Arena_Guard_2"] = new("Arena Guard", PlayerType.AIScav),
            ["Boss_Kaban"] = new("Kaban", PlayerType.AIBoss),
            ["Boss_Kollontay"] = new("Kollontay", PlayerType.AIBoss),
            ["Boss_Sturman"] = new("Shturman", PlayerType.AIBoss),
            ["Zombie_Generic"] = new("Zombie", PlayerType.AIScav),
            ["BossZombieTagilla"] = new("Zombie Tagilla", PlayerType.AIBoss),
            ["Zombie_Fast"] = new("Zombie", PlayerType.AIScav),
            ["Zombie_Medium"] = new("Zombie", PlayerType.AIScav),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Determines the AI role from a voice line string.
        /// Checks the frozen dictionary first, then falls back to pattern matching.
        /// Applies map-based overrides (e.g., laboratory → Raider).
        /// </summary>
        private AIRole GetInitialAIRole(string voiceLine)
        {
            if (string.IsNullOrEmpty(voiceLine))
                return new("Scav", PlayerType.AIScav);

            if (!_aiRolesByVoice.TryGetValue(voiceLine, out var role))
            {
                role = voiceLine switch
                {
                    _ when voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase) => new("Scav", PlayerType.AIScav),
                    _ when voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase) => new("Boss", PlayerType.AIBoss),
                    _ when voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase) => new("Raider", PlayerType.AIRaider),
                    _ when voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase) => new("Raider", PlayerType.AIRaider),
                    _ when voiceLine.Contains("black_division", StringComparison.OrdinalIgnoreCase) => new("BD", PlayerType.AIRaider),
                    _ when voiceLine.Contains("vsrf", StringComparison.OrdinalIgnoreCase) => new("Vsrf", PlayerType.AIRaider),
                    _ when voiceLine.Contains("civilian", StringComparison.OrdinalIgnoreCase) => new("Civ", PlayerType.AIScav),
                    _ => new("Scav", PlayerType.AIScav)
                };
            }

            // Labs override: all non-boss AI → Raider
            if (_mapId == "laboratory" && role.Type != PlayerType.AIBoss)
                role = new("Raider", PlayerType.AIRaider);

            return role;
        }

        #endregion
    }
}
