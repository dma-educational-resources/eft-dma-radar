/*
 * Lone EFT DMA Radar
 * MIT License
 *
 * TeammatesWorker
 * ----------------
 * - Single press toggle:
 *   - Add teammate if not present
 *   - Remove teammate if already present
 * - Restores original PlayerType on removal
 * - Restart-safe via JSON (VoipId + OriginalType)
 * - Raid-safe via SessionID
 */

using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using System.IO;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using static SDK.Enums;

namespace eft_dma_radar.Tarkov.EFTPlayer.Plugins
{
    public sealed class TeammatesWorker
    {
        static TeammatesWorker()
        {
            new TeammatesWorker();
        }

        /* ==============================
         * CONFIG
         * ============================== */

        /// <summary>
        /// Set by hotkey (true on press, false on release).
        /// </summary>
        public static volatile bool Engaged;

        /* ==============================
         * STATE
         * ============================== */

        private sealed class TeammateEntry
        {
            public int VoipId { get; set; }
            public PlayerType OriginalType { get; set; }
        }

        private static readonly ConcurrentDictionary<int, TeammateEntry>
            _teammates = new();

        private static readonly string _filePath =
            Path.Combine(AppContext.BaseDirectory, "Teammates.json");

        private static string? _currentSessionId;
        private static bool _wasEngaged;

        /* ==============================
         * FILE FORMAT
         * ============================== */

        private sealed class TeammatesFile
        {
            public string? SessionId { get; set; }
            public List<TeammateEntry> Players { get; set; } = new();
        }

        /* ==============================
         * STARTUP
         * ============================== */

        public TeammatesWorker()
        {
            new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "TeammatesWorker"
            }.Start();
        }

        /* ==============================
         * MAIN WORKER
         * ============================== */

        private static void WorkerLoop()
        {
            XMLogging.WriteLine("[TeammatesWorker] Thread started");

            while (true)
            {
                try
                {
                    if (!MemDMABase.WaitForRaid() ||
                        Memory.Game is not LocalGameWorld game ||
                        !IsActuallyInRaid())
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    OnRaidStart();

                    while (game.InRaid)
                    {
                        Tick(game);
                        Thread.Sleep(1);
                    }

                    OnRaidEnd();
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[TeammatesWorker] CRITICAL ERROR: {ex}");
                    Thread.Sleep(500);
                }
            }
        }

        /* ==============================
         * RAID LIFECYCLE
         * ============================== */

        private static void OnRaidStart()
        {
            if (!IsActuallyInRaid())
                return;

            string? sessionId = GetSessionId();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                XMLogging.WriteLine(
                    "[TeammatesWorker] SessionID invalid but in raid ¨C preserving list");
                return;
            }

            var file = LoadFile();

            if (file.SessionId == sessionId)
            {
                XMLogging.WriteLine(
                    $"[TeammatesWorker] Resuming raid {sessionId}");

                _currentSessionId = sessionId;
                _teammates.Clear();

                foreach (var e in file.Players)
                    _teammates.TryAdd(e.VoipId, e);

                return;
            }

            XMLogging.WriteLine(
                $"[TeammatesWorker] New raid {sessionId}");

            _currentSessionId = sessionId;
            _teammates.Clear();
            Save();
        }

        private static void OnRaidEnd()
        {
            if (!IsActuallyInRaid())
                return;

            ResetState();
            XMLogging.WriteLine(
                "[TeammatesWorker] Raid ended ¨C cleared Teammates.json");
        }

        private static void ResetState()
        {
            _teammates.Clear();
            _currentSessionId = null;
            _wasEngaged = false;

            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch { }
        }
        public static void ForceAdd(Player player)
        {
            if (player == null || player.VoipId <= 0)
                return;

            if (_teammates.ContainsKey(player.VoipId))
                return;

            var entry = new TeammateEntry
            {
                VoipId = player.VoipId,
                OriginalType = player.Type
            };

            if (_teammates.TryAdd(player.VoipId, entry))
            {
                player.UpdatePlayerType(PlayerType.Teammate);
                Save();
            }
        }

        public static void ForceRemove(int voipId)
        {
            if (_teammates.TryRemove(voipId, out var entry))
            {
                // Type restoration happens automatically on next AutoFlag
                Save();
            }
        }

        /* ==============================
         * PER-FRAME LOGIC
         * ============================== */

        private static void Tick(LocalGameWorld game)
        {
            bool engaged = Volatile.Read(ref Engaged);

            // Always enforce teammate flag
            AutoFlagPlayers(game);

            if (!engaged)
            {
                _wasEngaged = false;
                return;
            }

            // Rising edge only
            if (_wasEngaged)
                return;

            _wasEngaged = true;

            if (Memory.LocalPlayer is not LocalPlayer lp)
                return;

            var target = GetTargetUnderCrosshair(game, lp);
            if (target == null || target.VoipId <= 0)
                return;

            ToggleTeammate(target, game);
        }

        /* ==============================
         * TOGGLE LOGIC
         * ============================== */

        private static void ToggleTeammate(Player target, LocalGameWorld game)
        {
            int voipId = target.VoipId;

            // REMOVE
            if (_teammates.TryRemove(voipId, out var entry))
            {
                target.UpdatePlayerType(entry.OriginalType);

                XMLogging.WriteLine(
                    $"[TeammatesWorker] Removed teammate VoipID={voipId}, restored={entry.OriginalType}");

                Save();
                return;
            }

            // ADD
            var newEntry = new TeammateEntry
            {
                VoipId = voipId,
                OriginalType = target.Type
            };

            if (_teammates.TryAdd(voipId, newEntry))
            {
                XMLogging.WriteLine(
                    $"[TeammatesWorker] Added teammate VoipID={voipId}, original={target.Type}");

                Save();
            }
        }

        /* ==============================
         * AUTO FLAG
         * ============================== */

        private static void AutoFlagPlayers(LocalGameWorld game)
        {
            var players = game.Players;
            if (players == null)
                return;
        
            foreach (var p in players)
            {
                if (p == null || p.VoipId <= 0)
                    continue;
        
                // CASE 1: Latched teammate ¡ú force Teammate
                if (_teammates.TryGetValue(p.VoipId, out var entry))
                {
                    if (p.Type != PlayerType.Teammate)
                    {
                        p.UpdatePlayerType(PlayerType.Teammate);
                    }
                    continue;
                }
        
                // CASE 2: Previously latched but now removed ¡ú restore
                if (p.Type == PlayerType.Teammate)
                {
                    RestoreOriginalType(p);
                }
            }
        }
        private static void RestoreOriginalType(Player p)
        {
            // Re-run base classification safely
            if (p is ObservedPlayer op)
            {
                if (op.IsPmc)
                {
                    if (op.PlayerSide == EPlayerSide.Usec)
                        p.UpdatePlayerType(PlayerType.USEC);
                    else if (op.PlayerSide == EPlayerSide.Bear)
                        p.UpdatePlayerType(PlayerType.BEAR);
                    else
                        p.UpdatePlayerType(PlayerType.Default);
                }
                else if (op.IsScav)
                {
                    p.UpdatePlayerType(PlayerType.PScav);
                }
                else if (op.IsAI)
                {
                    p.UpdatePlayerType(PlayerType.AIScav);
                }
                else
                {
                    p.UpdatePlayerType(PlayerType.Default);
                }
            }
        }

        /* ==============================
         * TARGET SELECTION
         * ============================== */

        private static Player? GetTargetUnderCrosshair(
            LocalGameWorld game,
            LocalPlayer localPlayer)
        {
            var players = game.Players?
                .Where(p => p.IsActive && p != localPlayer);

            if (players == null)
                return null;

            Player? best = null;
            float bestFov = float.MaxValue;

            foreach (var player in players)
            {
                if (player.Skeleton?.Bones == null)
                    continue;

                foreach (var bone in player.Skeleton.Bones)
                {
                    if (bone.Key is Bones.HumanHead)
                        continue;

                    Vector3 pos = bone.Value.Position;

                    if (!CameraManagerBase.WorldToScreen(
                            ref pos, out var screen, true))
                        continue;

                    float fov = CameraManagerBase.GetFovMagnitude(screen);
                    if (fov > 30.0f)
                        continue;

                    if (fov < bestFov)
                    {
                        bestFov = fov;
                        best = player;
                    }
                }
            }

            return best;
        }

        /* ==============================
         * SESSION / RAID CHECKS
         * ============================== */

        private static string? GetSessionId()
        {
            try
            {
                ulong unityBase = Memory.UnityBase;
                ulong gomAddr = GameObjectManager.GetAddr(unityBase);
                var gom = GameObjectManager.Get(gomAddr);

                ulong app = gom.FindBehaviourByClassName("TarkovApplication");
                app.ThrowIfInvalidVirtualAddress();

                ulong menuOp = Memory.ReadPtr(app + Offsets.TarkovApplication._menuOperation);
                ulong ui = Memory.ReadPtr(menuOp + 0x60);
                ulong txt = Memory.ReadPtr(ui + 0x118);

                return Memory.ReadUnityString(txt);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsActuallyInRaid()
        {
            bool ready      = Memory.Ready;
            bool inRaid     = Memory.InRaid;
            bool hasLocal   = Memory.LocalPlayer is not null;
            bool handsValid = hasLocal &&
                              Memory.LocalPlayer.Firearm.HandsController.Item1
                                  .IsValidVirtualAddress();

            return ready && inRaid && hasLocal && handsValid;
        }

        /* ==============================
         * FILE IO
         * ============================== */

        private static TeammatesFile LoadFile()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new TeammatesFile();

                return JsonSerializer.Deserialize<TeammatesFile>(
                    File.ReadAllText(_filePath)) ?? new TeammatesFile();
            }
            catch
            {
                return new TeammatesFile();
            }
        }

        private static void Save()
        {
            try
            {
                var file = new TeammatesFile
                {
                    SessionId = _currentSessionId,
                    Players = _teammates.Values.ToList()
                };

                File.WriteAllText(
                    _filePath,
                    JsonSerializer.Serialize(file, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }));
            }
            catch { }
        }

        /* ==============================
         * PUBLIC API
         * ============================== */

        public static bool IsTeammate(Player p)
            => p != null &&
               p.VoipId > 0 &&
               _teammates.ContainsKey(p.VoipId);
    }
}
