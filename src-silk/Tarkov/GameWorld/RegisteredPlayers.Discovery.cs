using eft_dma_radar.Silk.Tarkov.Unity;
using System.Collections.Frozen;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;
using static SDK.Offsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    internal sealed partial class RegisteredPlayers
    {
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

                Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] CreatePlayerEntry 0x{playerBase:X} isLocal={isLocal} class='{className ?? "<null>"}' isObserved={isObserved}");

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
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   AI scav: voice='{voice ?? "<null>"}' → {role.Name} ({role.Type})");
                        }
                        else
                        {
                            // Player scav
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = $"PScav{id}";
                            type = PlayerType.PScav;
                            Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   Player scav: id={id}");
                        }
                    }
                    else
                    {
                        // PMC (USEC/BEAR)
                        var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                        var side = sideRaw == 1 ? "Usec" : "Bear";
                        name = $"{side}{id}";
                        type = sideRaw == 1 ? PlayerType.USEC : PlayerType.BEAR;
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   PMC: side={sideRaw} ({side}), id={id}");
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
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers]   Client player: name='{name}' side={sideRaw} type={type}");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] Rejected player 0x{playerBase:X}: empty name (class='{className}', observed={isObserved})");
                    return null;
                }

                Player.Player player = isLocal
                    ? CreateLocalPlayer(playerBase, name, type, sideRaw)
                    : new Player.Player { Name = name, Type = type, IsAlive = true, IsActive = true };

                player.Base = playerBase;
                var entry = new PlayerEntry(playerBase, player, isObserved);

                // Track in player history (non-local, human players only — filtered inside AddOrUpdate)
                Memory.PlayerHistory.AddOrUpdate(player);

                // Promote to SpecialPlayer if on the watchlist (requires AccountId, so
                // this only works for players whose AccountId is already resolved at
                // discovery time — re-checked later when AccountId becomes available).
                if (!isLocal && player.IsHuman)
                    CheckWatchlist(player);

                // Stagger initial gear/hands refresh times so newly discovered players
                // don't all fire in the same registration tick (thundering herd).
                // Each player gets an incrementing slot that spaces out refreshes.
                int slot = _staggerIndex++;
                var now = DateTime.UtcNow;
                entry.NextGearRefresh = now.AddMilliseconds(slot * 250);
                entry.NextHandsRefresh = now.AddMilliseconds(slot * 150);
                entry.NextHealthRefresh = now.AddMilliseconds(slot * 200);

                // Resolve ObservedHealthController for observed players (used for health status reads).
                // Non-fatal: if it fails now, health will simply show as Healthy until a retry succeeds.
                if (isObserved)
                    TryResolveObservedHealthController(playerBase, entry);

                // Transform + rotation init is deferred to BatchInitTransformsAndRotations()
                // which runs after all new players are discovered in a single batched scatter.
                // For the local player (single init) we still do it inline.
                if (isLocal)
                {
                    TryInitTransform(playerBase, entry);
                    TryInitRotation(playerBase, entry);

                    // Cache IsAiming address for batched scatter reads (CameraManager ADS detection)
                    if (player is LocalPlayer lp2 && lp2.PWA != 0)
                        entry.IsAimingAddr = lp2.PWA + Offsets.ProceduralWeaponAnimation._isAiming;
                }

                Log.WriteLine($"[RegisteredPlayers] Discovered: {player} @ 0x{playerBase:X} (class='{className}', observed={isObserved}, " +
                    $"transformReady={entry.TransformReady}, rotationReady={entry.RotationReady}, pos={player.Position})");

                return entry;
            }
            catch (Exception ex)
            {
                // Rate-limit per-pointer — the registration worker retries failed addresses
                // on a backoff schedule and will log the same address repeatedly otherwise.
                Log.WriteRateLimited(AppLogLevel.Warning, $"create_entry_{playerBase:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] CreatePlayerEntry FAILED 0x{playerBase:X} isLocal={isLocal}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks the watchlist for a human player and promotes to <see cref="PlayerType.SpecialPlayer"/>
        /// if a match is found. Called at discovery and again when AccountId is resolved.
        /// </summary>
        private static void CheckWatchlist(Player.Player player)
        {
            if (player.Type is PlayerType.SpecialPlayer or PlayerType.Streamer)
                return; // already flagged

            var match = Memory.PlayerWatchlist.Lookup(player.AccountId);
            if (match is not null)
            {
                player.Type = PlayerType.SpecialPlayer;
                Log.WriteLine($"[RegisteredPlayers] Watchlist match: {player.Name} → SpecialPlayer (reason: {match.Reason})");
            }
        }

        #endregion

        #region Health Controller Resolution

        /// <summary>
        /// Resolves the ObservedHealthController address for an observed player.
        /// Chain: ObservedPlayerView → ObservedPlayerController → HealthController.
        /// Validates via the Player backref at ObservedHealthController.Player.
        /// </summary>
        private static void TryResolveObservedHealthController(ulong playerBase, PlayerEntry entry)
        {
            try
            {
                if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                    || !opc.IsValidVirtualAddress())
                    return;

                if (!Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.HealthController, out var ohc, false)
                    || !ohc.IsValidVirtualAddress())
                    return;

                // Validate: ObservedHealthController.Player should point back to the player base
                if (!Memory.TryReadValue<ulong>(ohc + Offsets.ObservedHealthController.Player, out var playerBackref, false)
                    || playerBackref != playerBase)
                    return;

                entry.ObservedHealthControllerAddr = ohc;
                Log.Write(AppLogLevel.Debug,
                    $"[RegisteredPlayers] ObservedHealthController resolved for '{entry.Player.Name}': OHC=0x{ohc:X}");
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Debug,
                    $"[RegisteredPlayers] ObservedHealthController resolve failed for '{entry.Player.Name}': {ex.Message}");
            }
        }

        #endregion

        #region Local Player Factory

        /// <summary>
        /// Creates a <see cref="LocalPlayer"/> with PMC/Scav identity data for exfil eligibility.
        /// </summary>
        private static LocalPlayer CreateLocalPlayer(ulong playerBase, string name, PlayerType type, int sideRaw)
        {
            var lp = new LocalPlayer
            {
                Name = name,
                Type = type,
                IsAlive = true,
                IsActive = true,
                IsPmc = sideRaw is 1 or 2,    // USEC=1, BEAR=2
                IsScav = sideRaw == 4,          // Savage=4
            };

            try
            {
                var profilePtr = Memory.ReadPtr(playerBase + Offsets.Player.Profile, false);
                var infoPtr = Memory.ReadPtr(profilePtr + Offsets.Profile.Info, false);

                // Store profile pointer for QuestManager
                lp.ProfilePtr = profilePtr;

                // Resolve ProceduralWeaponAnimation pointer (for CameraManager scope detection)
                if (Memory.TryReadPtr(playerBase + Offsets.Player.ProceduralWeaponAnimation, out var pwa))
                {
                    lp.PWA = pwa;
                    Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] LocalPlayer PWA @ 0x{pwa:X}");
                }

                // Entry point (PMC exfil eligibility)
                if (lp.IsPmc)
                {
                    if (Memory.TryReadPtr(infoPtr + Offsets.PlayerInfo.EntryPoint, out var entryPtr)
                        && Memory.TryReadUnityString(entryPtr, out var entryPoint))
                    {
                        lp.EntryPoint = entryPoint;
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] LocalPlayer entry point: '{entryPoint}'");
                    }
                }

                // Profile ID (Scav exfil eligibility)
                if (lp.IsScav)
                {
                    if (Memory.TryReadPtr(profilePtr + Offsets.Profile.Id, out var profileIdPtr)
                        && Memory.TryReadUnityString(profileIdPtr, out var profileId))
                    {
                        lp.LocalProfileId = profileId;
                        Log.Write(AppLogLevel.Debug, $"[RegisteredPlayers] LocalPlayer profile ID: '{profileId}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write(AppLogLevel.Warning, $"[RegisteredPlayers] Failed to read LocalPlayer identity: {ex.Message}");
            }

            return lp;
        }

        #endregion

        #region Helpers

        private static string? ReadClassName(ulong playerBase)
        {
            try
            {
                return Unity.Il2CppClass.ReadName(playerBase, 64);
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
