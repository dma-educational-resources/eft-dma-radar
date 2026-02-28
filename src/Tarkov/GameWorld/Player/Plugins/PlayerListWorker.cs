/*
 * Lone EFT DMA Radar
 * MIT License
 *
 * PlayerListWorker
 * ----------------
 * - Stable PMC naming per raid (U:PMC1 / B:PMC1)
 * - Squad grouping via spawn proximity (¡Ü20m)
 * - Late-spawn safe
 * - VoipID identity
 * - SessionID scoped
 * - FILE IS TOUCHED ONLY WHEN SESSION ID CHANGES
 */

using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using System.IO;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.EFTPlayer;
using static SDK.Enums;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    [System.Reflection.Obfuscation(
        Exclude = true,
        ApplyToMembers = true,
        Feature = "all"
    )]    
    public sealed class PlayerListWorker
    {
        static PlayerListWorker()
        {
            new PlayerListWorker();
        }

        private const float GROUP_DISTANCE_METERS = 15f;
        private const float GROUP_DISTANCE_SQR = GROUP_DISTANCE_METERS * GROUP_DISTANCE_METERS;

        /* ==============================
         * FILE MODELS
         * ============================== */

        private sealed class PlayerEntry
        {
            public string ProfileId { get; set; } = string.Empty;
            public EPlayerSide Side { get; set; }
            public int PmcIndex { get; set; }
            public int GroupId { get; set; }
            public Vector3 Spawn { get; set; }
            public string? Nickname { get; set; }
            public string? AccountId { get; set; }
        }

        private sealed class PlayerListFile
        {
            public string? SessionId { get; set; }
            public int NextGroupId { get; set; } = 1;
            public List<PlayerEntry> Players { get; set; } = new();
        }

        /* ==============================
         * STATE
         * ============================== */

        private static readonly ConcurrentDictionary<string, PlayerEntry> _players = new();
        private static int _usecCounter;
        private static int _bearCounter;
        private static int _nextGroupId = 1;
        private static string? _currentSessionId;

        private static readonly object _lock = new();

        private static readonly string _filePath =
            Path.Combine(AppContext.BaseDirectory, "PlayerList.json");

        /* ==============================
         * STARTUP
         * ============================== */

        private PlayerListWorker()
        {
            new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "PlayerListWorker"
            }.Start();
        }

        /* ==============================
         * MAIN LOOP
         * ============================== */

        private static void WorkerLoop()
        {
            while (true)
            {
                try
                {
                    if (Memory.Game is not LocalGameWorld game || !Memory.InRaid)
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    OnRaidStart(game);

                    while (game.InRaid)
                        Thread.Sleep(100);

                    ResetMemoryOnly();
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        }

        /* ==============================
         * PUBLIC API
         * ============================== */

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory",
            "Labyrinth"
        };

        public static string? GetOrAssignDisplayName(ObservedPlayer p)
        {
            if (p == null || !p.IsPmc || string.IsNullOrEmpty(p.ProfileID))
                return null;

            var local = Memory.LocalPlayer;
            if (local == null)
                return null;

            if (IsLocalSquadMember(p, local))
                return null;

            if (Memory.Game is not LocalGameWorld game)
                return null;

            if (ExcludedMaps.Contains(game.MapID))
                return null;

            Vector3 pos = p.Position;
            if (!IsValidSpawn(pos))
                return null;

            lock (_lock)
            {
                if (!_players.TryGetValue(p.ProfileID, out var entry))
                {
                    int groupId = FindOrCreateGroup(pos);

                    int pmcIndex =
                        p.PlayerSide == EPlayerSide.Usec
                            ? ++_usecCounter
                            : ++_bearCounter;

                    entry = new PlayerEntry
                    {
                        ProfileId = p.ProfileID,
                        Side = p.PlayerSide,
                        PmcIndex = pmcIndex,
                        GroupId = groupId,
                        Spawn = pos
                    };

                    _players[p.ProfileID] = entry;
                    Save();
                }

                return entry.Side == EPlayerSide.Usec
                    ? $"U:PMC{entry.PmcIndex}"
                    : $"B:PMC{entry.PmcIndex}";
            }
        }

        public static bool TryGetIdentity(
            string profileId,
            out string? nickname,
            out string? accountId)
        {
            nickname = null;
            accountId = null;

            if (_players.TryGetValue(profileId, out var entry))
            {
                nickname = entry.Nickname;
                accountId = entry.AccountId;
                return !string.IsNullOrEmpty(nickname);
            }

            return false;
        }

        public static void UpdateIdentity(
            string profileId,
            string nickname,
            string accountId)
        {
            if (string.IsNullOrEmpty(profileId))
                return;

            lock (_lock)
            {
                if (!_players.TryGetValue(profileId, out var entry))
                {
                    entry = new PlayerEntry
                    {
                        ProfileId = profileId,
                        Nickname = nickname,
                        AccountId = accountId
                    };

                    _players[profileId] = entry;
                    Save();
                    return;
                }


                bool changed = false;

                if (!string.IsNullOrEmpty(nickname) && entry.Nickname != nickname)
                {
                    entry.Nickname = nickname;
                    changed = true;
                }

                if (!string.IsNullOrEmpty(accountId) && entry.AccountId != accountId)
                {
                    entry.AccountId = accountId;
                    changed = true;
                }

                if (changed)
                    Save();
            }
        }

        /* ==============================
         * GROUP LOGIC
         * ============================== */

        private static int FindOrCreateGroup(Vector3 spawn)
        {
            foreach (var p in _players.Values)
            {
                if (Vector3.DistanceSquared(p.Spawn, spawn) <= GROUP_DISTANCE_SQR)
                    return p.GroupId;
            }

            return _nextGroupId++;
        }

        private static bool IsValidSpawn(Vector3 v) =>
            v != Vector3.Zero &&
            !float.IsNaN(v.X) &&
            !float.IsNaN(v.Y) &&
            !float.IsNaN(v.Z);

        private static bool IsLocalSquadMember(ObservedPlayer p, LocalPlayer local)
        {
            if (p.NetworkGroupID != -1 &&
                p.NetworkGroupID == local.NetworkGroupID)
                return true;

            Vector3 lpPos = local.Position;
            return IsValidSpawn(lpPos) &&
                   Vector3.DistanceSquared(lpPos, p.Position) <= GROUP_DISTANCE_SQR;
        }

        /* ==============================
         * RAID LIFECYCLE
         * ============================== */

        private static void OnRaidStart(LocalGameWorld game)
        {
            string? sessionId = GetSessionId();
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            lock (_lock)
            {
                var file = LoadFile();

                InitForSession(sessionId);

                if (file.SessionId == sessionId)
                {
                    _nextGroupId = file.NextGroupId;

                    foreach (var p in file.Players)
                    {
                        if (!IsValidSpawn(p.Spawn))
                            continue;

                        _players[p.ProfileId] = p;

                        if (p.Side == EPlayerSide.Usec)
                            _usecCounter = Math.Max(_usecCounter, p.PmcIndex);
                        else
                            _bearCounter = Math.Max(_bearCounter, p.PmcIndex);
                    }
                }

                Save(); // <-- GUARANTEE FILE EXISTS
            }
        }

        public static int GetOrAssignSpawnGroup(
            string profileId,
            Vector3 spawn,
            EPlayerSide side)
        {
            if (string.IsNullOrEmpty(profileId) || !IsValidSpawn(spawn))
                return -1;
        
            lock (_lock)
            {
                if (_players.TryGetValue(profileId, out var existing))
                    return existing.GroupId;
        
                int groupId = FindOrCreateGroup(spawn);
        
                var entry = new PlayerEntry
                {
                    ProfileId = profileId,
                    Side = side,
                    GroupId = groupId,
                    Spawn = spawn
                };
        
                _players[profileId] = entry;
                Save();
        
                return groupId;
            }
        }

        private static void InitForSession(string sessionId)
        {
            _players.Clear();
            _usecCounter = 0;
            _bearCounter = 0;
            _nextGroupId = 1;
            _currentSessionId = sessionId;
        }

        private static void ResetMemoryOnly()
        {
            _players.Clear();
            _usecCounter = 0;
            _bearCounter = 0;
            _nextGroupId = 1;
            _currentSessionId = null;
        }

        /* ==============================
         * FILE IO
         * ============================== */

        private static PlayerListFile LoadFile()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new PlayerListFile();

                return JsonSerializer.Deserialize<PlayerListFile>(
                    File.ReadAllText(_filePath)) ?? new PlayerListFile();
            }
            catch
            {
                return new PlayerListFile();
            }
        }

        private static void Save()
        {
            try
            {
                var file = new PlayerListFile
                {
                    SessionId = _currentSessionId,
                    NextGroupId = _nextGroupId,
                    Players = _players.Values.ToList()
                };

                File.WriteAllText(
                    _filePath,
                    JsonSerializer.Serialize(file,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /* ==============================
         * SESSION ID
         * ============================== */

        private static string? GetSessionId()
        {
            try
            {
                ulong unityBase = Memory.UnityBase;
                ulong gomAddr = GameObjectManager.GetAddr(unityBase);
                var gom = GameObjectManager.Get(gomAddr);

                ulong app = gom.FindBehaviourByClassName("TarkovApplication");
                ulong menuOp = Memory.ReadPtr(app + Offsets.TarkovApplication._menuOperation);
                ulong ui = Memory.ReadPtr(menuOp + Offsets.MainMenuShowOperation._preloaderUI);
                ulong txt = Memory.ReadPtr(ui + Offsets.PreloaderUI._sessionIdText);

                return Memory.ReadUnityString(txt);
            }
            catch
            {
                return null;
            }
        }
    }
}
