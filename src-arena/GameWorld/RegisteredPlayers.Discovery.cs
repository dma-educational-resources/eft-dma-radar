using System.Collections.Frozen;
using eft_dma_radar.Arena.DMA;
using eft_dma_radar.Arena.Unity;
using eft_dma_radar.Arena.Unity.Collections;
using eft_dma_radar.Arena.Unity.IL2CPP;
using SDK;

using static eft_dma_radar.Arena.Unity.UnityOffsets;

namespace eft_dma_radar.Arena.GameWorld
{
    internal sealed partial class RegisteredPlayers
    {
        // ΓöÇΓöÇ Discovery helpers ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

        private Player? CreatePlayerEntry(ulong playerBase, bool isLocal)
        {
            try
            {
                string name;
                string? accountId = null;
                string? profileId = null;
                PlayerType type;
                bool isAI = false;
                int teamId = -1;
                bool nickSucceeded = true;
                bool typeSucceeded = true;

                if (isLocal)
                {
                    // Arena doesn't expose a readable nickname for the local player ΓÇö the Profile
                    // chain is deep and version-dependent. Use a fixed label for now.
                    name = "LocalPlayer";
                    type = PlayerType.LocalPlayer;

                    // Arena TeamID (armband color) via Player._inventoryController.
                    // We do NOT lock _matchLocalTeamId here ΓÇö the very first armband read at
                    // match start is the most likely to be wrong (slot still being populated /
                    // stale ContainedItem from the reused previous-round GameWorld). Only seed
                    // an initial value; the stability gate in UpdateExistingPlayers requires N
                    // consecutive matching reads before promoting it to _matchLocalTeamId.
                    try
                    {
                        if (_matchLocalTeamId >= 0)
                        {
                            // Already locked from a previous tick (e.g. respawn rebuilds entry).
                            teamId = _matchLocalTeamId;
                        }
                        else if (Memory.TryReadPtr(playerBase + Offsets.Player._inventoryController, out var invCtrl, false)
                                 && invCtrl.IsValidVirtualAddress())
                        {
                            teamId = GetTeamIDForDiscovery(playerBase, isLocal: true, invCtrl);
                            // Note: stability streak is built/extended in UpdateExistingPlayers,
                            // not here ΓÇö we don't want a single discovery-time read to count.
                        }
                    }
                    catch { }
                }
                else
                {
                    // Observed player (ObservedPlayerView hierarchy)
                    int sideRaw = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Side, false);
                    isAI = Memory.ReadValue<bool>(playerBase + Offsets.ObservedPlayerView.IsAI, false);

                    // Nickname
                    if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.NickName, out var nickPtr, false)
                        && nickPtr.IsValidVirtualAddress())
                    {
                        name = Memory.ReadUnityString(nickPtr, 64, false);
                    }
                    else
                    {
                        name = string.Empty;
                    }
                    nickSucceeded = !string.IsNullOrWhiteSpace(name);
                    typeSucceeded = sideRaw != 0;

                    // AccountId: not populated by Arena's server ΓÇö skipped.
                    // ProfileId (optional)
                    if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ProfileId, out var profPtr, false)
                        && profPtr.IsValidVirtualAddress())
                    {
                        var prof = Memory.ReadUnityString(profPtr, 64, false);
                        if (!string.IsNullOrEmpty(prof))
                            profileId = prof;
                    }

                    // If nickname is empty, use voice-based role for AI or ID-based fallback
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        if (isAI)
                        {
                            var voice = TryReadVoiceLine(playerBase);
                            name = GetAIName(voice ?? string.Empty);
                        }
                        else
                        {
                            var id = Memory.ReadValue<int>(playerBase + Offsets.ObservedPlayerView.Id, false);
                            name = sideRaw == 4 ? $"PScav{id}" : $"PMC{id}";
                        }
                    }

                    type = isAI
                        ? GetAIType(TryReadVoiceLine(playerBase) ?? string.Empty)
                        : FactionFromSide(sideRaw);

                    // Arena TeamID (armband color) via ObservedPlayerController -> InventoryController.
                    // Only meaningful for humans (AI don't have armbands).
                    if (!isAI)
                    {
                        try
                        {
                            if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                                && opc.IsValidVirtualAddress()
                                && Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.InventoryController, out var invCtrl, false)
                                && invCtrl.IsValidVirtualAddress())
                            {
                                teamId = GetTeamIDForDiscovery(playerBase, isLocal: false, invCtrl);
                            }
                        }
                        catch { }

                        // Teammate classification ΓÇö matches local player's match-locked team.
                        int localTeam = _matchLocalTeamId >= 0
                            ? _matchLocalTeamId
                            : (LocalPlayer?.TeamID ?? -1);
                        if (teamId != -1 && localTeam >= 0 && localTeam == teamId)
                            type = PlayerType.Teammate;
                    }
                }

                var player = new Player
                {
                    Base        = playerBase,
                    Name        = name,
                    AccountId   = accountId,
                    ProfileId   = profileId,
                    Type        = type,
                    IsLocalPlayer = isLocal,
                    IsAI        = isAI,
                    TeamID      = teamId,
                    IsActive    = true,
                    IsAlive     = true,
                    NameResolved = nickSucceeded,
                    TypeResolved = typeSucceeded,
                };

                if (Log.EnableDebugLogging)
                    DumpPlayerHierarchy(playerBase, player.Name, isLocal);

                return player;
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"create_{playerBase:X}", TimeSpan.FromSeconds(5),
                    $"[RegisteredPlayers] CreatePlayerEntry FAILED 0x{playerBase:X}: {ex.Message}");
                return null;
            }
        }

        private static string? TryReadVoiceLine(ulong playerBase)
        {
            try
            {
                if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.Voice, out var voicePtr, false)
                    || !voicePtr.IsValidVirtualAddress())
                    return null;
                return Memory.ReadUnityString(voicePtr, 64, false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Dumps the full IL2CPP hierarchy for a newly-discovered player and all relevant
        /// sub-objects (OPC, MovementController, HealthController, InventoryController, PlayerBody).
        /// Only called when <see cref="Log.EnableDebugLogging"/> is <c>true</c>.
        /// </summary>
        private static void DumpPlayerHierarchy(ulong playerBase, string name, bool isLocal)
        {
            try
            {
                if (isLocal)
                {
                    Il2CppDumper.DumpClassFields(playerBase, $"LocalPlayer (EFT.Player) '{name}'");

                    // MovementContext
                    if (Memory.TryReadPtr(playerBase + Offsets.Player.MovementContext, out var mc, false) && mc.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(mc, $"LocalPlayer MovementContext '{name}'");

                    // InventoryController
                    if (Memory.TryReadPtr(playerBase + Offsets.Player._inventoryController, out var invCtrl, false) && invCtrl.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(invCtrl, $"LocalPlayer InventoryController '{name}'");
                }
                else
                {
                    // ObservedPlayerView
                    Il2CppDumper.DumpClassFields(playerBase, $"ObservedPlayer (OPV) '{name}'");

                    if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                        || !opc.IsValidVirtualAddress())
                        return;

                    // ObservedPlayerController
                    Il2CppDumper.DumpClassFields(opc, $"ObservedPlayerController '{name}'");

                    // HealthController
                    if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.HealthController, out var hc, false) && hc.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(hc, $"ObservedHealthController '{name}'");

                    // MovementController → StateContext
                    if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.MovementController, out var mc, false) && mc.IsValidVirtualAddress())
                    {
                        Il2CppDumper.DumpClassFields(mc, $"ObservedMovementController '{name}'");
                        if (Memory.TryReadPtr(mc + Offsets.ObservedMovementController.StateContext, out var sc, false) && sc.IsValidVirtualAddress())
                            Il2CppDumper.DumpClassFields(sc, $"ObservedPlayerStateContext '{name}'");
                    }

                    // InventoryController
                    if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.InventoryController, out var invCtrl, false) && invCtrl.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(invCtrl, $"ObservedInventoryController '{name}'");

                    // Full stability objects (position chain, bone chain)
                    DumpStabilityObjects(playerBase, name);
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[RegisteredPlayers] DumpPlayerHierarchy failed for '{name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Dumps the objects most relevant to position and skeleton stability:
        /// Culling, BifacialTransform, LookTransform, PlayerBones, DizSkinningSkeleton (root + hands).
        /// Called at discovery time (via <see cref="DumpPlayerHierarchy"/>) AND periodically
        /// throughout the match via <see cref="BatchPeriodicDumps"/> so post-respawn state changes
        /// are captured automatically without needing F8.
        /// </summary>
        private static void DumpStabilityObjects(ulong playerBase, string name)
        {
            try
            {
                if (!Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.ObservedPlayerController, out var opc, false)
                    || !opc.IsValidVirtualAddress())
                    return;

                // Culling — may show why hierarchy transforms go stale (culled players stop updating)
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.Culling, out var culling, false) && culling.IsValidVirtualAddress())
                    Il2CppDumper.DumpClassFields(culling, $"OPC.Culling '{name}'");

                // BifacialTransform from StateContext._playerTransform — alternative stable position source
                if (Memory.TryReadPtr(opc + Offsets.ObservedPlayerController.MovementController, out var mcBf, false) && mcBf.IsValidVirtualAddress()
                    && Memory.TryReadPtr(mcBf + Offsets.ObservedMovementController.StateContext, out var scBf, false) && scBf.IsValidVirtualAddress()
                    && Memory.TryReadPtr(scBf + Offsets.ObservedPlayerStateContext._playerTransform, out var bifacial, false) && bifacial.IsValidVirtualAddress())
                    Il2CppDumper.DumpClassFields(bifacial, $"StateContext.BifacialTransform '{name}'");

                // lookTransform managed object — the transform we walk for the realtime position read
                if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView._playerLookRaycastTransform, out var lookTransform, false) && lookTransform.IsValidVirtualAddress())
                    Il2CppDumper.DumpClassFields(lookTransform, $"LookTransform (managed) '{name}'");

                // PlayerBody sub-objects — bone tree layout
                if (Memory.TryReadPtr(playerBase + Offsets.ObservedPlayerView.PlayerBody, out var body, false) && body.IsValidVirtualAddress())
                {
                    Il2CppDumper.DumpClassFields(body, $"PlayerBody '{name}'");

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.PlayerBones, out var playerBones, false) && playerBones.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(playerBones, $"PlayerBones '{name}'");

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.SkeletonRootJoint, out var skelRoot, false) && skelRoot.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(skelRoot, $"DizSkinningSkeleton (SkeletonRootJoint) '{name}'");

                    if (Memory.TryReadPtr(body + Offsets.PlayerBody.SkeletonHands, out var skelHands, false) && skelHands.IsValidVirtualAddress())
                        Il2CppDumper.DumpClassFields(skelHands, $"DizSkinningSkeleton (SkeletonHands) '{name}'");
                }
            }
            catch (Exception ex)
            {
                Log.WriteRateLimited(AppLogLevel.Warning, $"stab_dump_{playerBase:X}", TimeSpan.FromSeconds(30),
                    $"[RegisteredPlayers] DumpStabilityObjects failed for '{name}': {ex.Message}");
            }
        }

        // ── AI role helpers

        // Armband template GUID -> Arena TeamID (ArmbandColorType).
        // Note: the red/blue GUIDs are labelled from the in-game team color, which is the

        // OPPOSITE of the raw item template name (the "red armband" template is worn by the
        // in-game "blue team" and vice versa). TeamID equality is what drives teammate
        // classification, so only the label would be affected either way ΓÇö these values
        // match what the player sees on-screen.
        private static readonly FrozenDictionary<string, int> _armbandTeamIds =
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["63615c104bc92641374a97c8"] = (int)ArmbandColorType.red,
                ["63615bf35cb3825ded0db945"] = (int)ArmbandColorType.fuchsia,
                ["63615c36e3114462cd79f7c1"] = (int)ArmbandColorType.yellow,
                ["63615bfc5cb3825ded0db947"] = (int)ArmbandColorType.green,
                ["63615bc6ff557272023d56ac"] = (int)ArmbandColorType.azure,
                ["63615c225cb3825ded0db949"] = (int)ArmbandColorType.white,
                ["63615be82e60050cb330ef2f"] = (int)ArmbandColorType.blue,
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>
        /// Returns the Arena TeamID derived from the player's ArmBand slot item template GUID.
        /// -1 if not found or read fails. Caches the ArmBand slot address on the <paramref name="player"/>
        /// (if provided) so subsequent reads skip the equipment slot scan.
        /// </summary>
        private static int GetTeamID(Player? player, ulong inventoryController)
            => GetTeamIDInternal(player, inventoryController, diagBase: player?.Base ?? 0, diagIsLocal: player?.IsLocalPlayer ?? false);

        /// <summary>
        /// Discovery-time variant: lets <see cref="CreatePlayerEntry"/> pass the playerBase and
        /// isLocal flag explicitly so the diagnostic log can label the read correctly even though
        /// no Player object exists yet.
        /// </summary>
        private static int GetTeamIDForDiscovery(ulong playerBase, bool isLocal, ulong inventoryController)
            => GetTeamIDInternal(player: null, inventoryController, diagBase: playerBase, diagIsLocal: isLocal);

        private static int GetTeamIDInternal(Player? player, ulong inventoryController, ulong diagBase, bool diagIsLocal)
        {
            try
            {
                string diagLabel = diagIsLocal ? "LOCAL" : "OBS";

                // Fast path: use cached ArmBand slot ptr if we've resolved it before.
                if (player is not null && player.ArmBandSlotAddr != 0)
                {
                    int teamFast = ReadArmbandTeamFromSlotDiag(player.ArmBandSlotAddr, diagBase, diagLabel);
                    if (teamFast >= 0) return teamFast;
                    // Slot went stale (respawn / equipment rebuild) ΓÇö fall through and rescan.
                    player.ArmBandSlotAddr = 0;
                }

                var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
                var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                var slots     = Memory.ReadPtr(equipment + Offsets.CompoundItem.Slots);

                using var slotsArray = MemArray<ulong>.Get(slots);
                foreach (var slotPtr in slotsArray)
                {
                    if (!slotPtr.IsValidVirtualAddress()) continue;
                    if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ID, out var slotNamePtr, false)
                        || !slotNamePtr.IsValidVirtualAddress()) continue;

                    if (Memory.ReadUnityString(slotNamePtr, 32, false) != "ArmBand") continue;

                    if (player is not null) player.ArmBandSlotAddr = slotPtr;
                    return ReadArmbandTeamFromSlotDiag(slotPtr, diagBase, diagLabel);
                }
            }
            catch { /* transient read failures are expected during player init */ }
            return -1;
        }

        // Probes the local Player object for the real <_inventoryController> field offset
        // by scanning every 8-byte aligned pointer slot in a plausible range and picking the
        // first one whose InventoryΓåÆEquipmentΓåÆSlots chain contains an "ArmBand" slot whose
        // ContainedItem template GUID is a known armband GUID. Caches the resolved offset.
        // Range 0x100..0x1000 covers all observed Arena Player layouts; 8-byte aligned.
        private void ProbeLocalInventoryControllerOffset(ulong playerBase)
        {
            // Don't latch _localInvCtrlOffsetProbed=true until we actually resolve. If the
            // armband isn't equipped yet at match start, the probe legitimately fails and we
            // want to keep retrying on subsequent registration ticks until it succeeds.

            const uint scanStart = 0x100;
            const uint scanEnd   = 0x1000;
            const uint scanStep  = 0x8;

            int candidates = 0;
            for (uint off = scanStart; off < scanEnd; off += scanStep)
            {
                if (!Memory.TryReadPtr(playerBase + off, out var invCtrl, false)) continue;
                if (!invCtrl.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(invCtrl + Offsets.InventoryController.Inventory, out var inventory, false)
                    || !inventory.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(inventory + Offsets.Inventory.Equipment, out var equipment, false)
                    || !equipment.IsValidVirtualAddress()) continue;
                if (!Memory.TryReadPtr(equipment + Offsets.CompoundItem.Slots, out var slots, false)
                    || !slots.IsValidVirtualAddress()) continue;

                MemArray<ulong>? slotsArray = null;
                try { slotsArray = MemArray<ulong>.Get(slots, false); }
                catch { continue; }
                if (slotsArray is null) continue;

                using (slotsArray)
                {
                    int slotCount = slotsArray.Count;
                    if (slotCount <= 0 || slotCount > 64) continue; // real equipment has ~10-15 slots

                    candidates++;
                    foreach (var slotPtr in slotsArray)
                    {
                        if (!slotPtr.IsValidVirtualAddress()) continue;
                        if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ID, out var slotNamePtr, false)
                            || !slotNamePtr.IsValidVirtualAddress()) continue;
                        string nm;
                        try { nm = Memory.ReadUnityString(slotNamePtr, 32, false); }
                        catch { continue; }
                        if (nm != "ArmBand") continue;

                        // Confirm by checking the ContainedItem template GUID is a known
                        // armband GUID (guards against false-positive chains).
                        int team = ReadArmbandTeamFromSlotDiag(slotPtr, playerBase, label: null);
                        if (team < 0) continue;

                        _localInvCtrlOffsetResolved = off;
                        _localInvCtrlOffsetProbed = true;
                        Log.WriteLine($"[RegisteredPlayers] LocalPlayer _inventoryController offset auto-resolved: base=0x{playerBase:X} +0x{off:X} (team={(ArmbandColorType)team}, candidates scanned={candidates})");
                        return;
                    }
                }
            }

            // Probe legitimately fails until the armband is equipped (start-of-match grace),
            // so this gets retried every team-id back-off tick. Keep it Debug + rate-limited
            // per-base so it doesn't dominate the log on every retry.
            Log.WriteRateLimited(AppLogLevel.Debug, $"local_invctrl_probe_{playerBase:X}", TimeSpan.FromSeconds(10),
                $"[RegisteredPlayers] LocalPlayer _inventoryController offset probe pending on base=0x{playerBase:X} (scanned 0x{scanStart:X}..0x{scanEnd:X}, candidates={candidates}); using hardcoded 0x{Offsets.Player._inventoryController:X}");
        }

        // Diagnostic variant: when label != null, logs the raw GUID + resolved team once per
        // (playerBase,label) pair every 30s so we can see WHY a player is being classified
        // the way they are without spamming the log.
        private static int ReadArmbandTeamFromSlotDiag(ulong slotPtr, ulong playerBase, string? label)
        {
            try
            {
                if (!Memory.TryReadPtr(slotPtr + Offsets.Slot.ContainedItem, out var containedItem, false)
                    || !containedItem.IsValidVirtualAddress())
                    return -1;

                var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                var mongo = Memory.ReadValue<Types.MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                var id = Memory.ReadUnityString(mongo.StringID, 64, false);

                if (string.IsNullOrEmpty(id)) return -1;
                bool known = _armbandTeamIds.TryGetValue(id, out var team);
                if (label is not null)
                {
                    string teamLabel = known ? ((ArmbandColorType)team).ToString() : "UNKNOWN";
                    Log.WriteRateLimited(AppLogLevel.Debug,
                        $"armband_diag_{playerBase:X}_{label}",
                        TimeSpan.FromSeconds(30),
                        $"[ArmbandDiag] {label} base=0x{playerBase:X} guid={id} -> {teamLabel}");
                }
                return known ? team : -1;
            }
            catch { return -1; }
        }

        private static PlayerType FactionFromSide(int sideRaw) => sideRaw switch
        {
            1 => PlayerType.USEC,
            2 => PlayerType.BEAR,
            4 => PlayerType.PScav,
            _ => PlayerType.Default,
        };

        // Back-off for TeamID resolution: ramp gently, capped at 5s so respawned players resolve fast.
        private static readonly FrozenDictionary<string, (string Name, PlayerType Type)> _aiRoles =
            new Dictionary<string, (string, PlayerType)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arena_Guard_1"]  = ("Arena Guard",  PlayerType.AIGuard),
                ["Arena_Guard_2"]  = ("Arena Guard",  PlayerType.AIGuard),
                ["BossSanitar"]    = ("Sanitar",       PlayerType.AIBoss),
                ["BossBully"]      = ("Reshala",       PlayerType.AIBoss),
                ["BossGluhar"]     = ("Gluhar",        PlayerType.AIBoss),
                ["SectantPriest"]  = ("Priest",        PlayerType.AIBoss),
                ["SectantWarrior"] = ("Cultist",       PlayerType.AIRaider),
                ["BossKilla"]      = ("Killa",         PlayerType.AIBoss),
                ["BossTagilla"]    = ("Tagilla",       PlayerType.AIBoss),
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static string GetAIName(string voice)
        {
            if (_aiRoles.TryGetValue(voice, out var role)) return role.Name;
            return voice.Contains("guard", StringComparison.OrdinalIgnoreCase) ? "Guard" : "Bot";
        }

        private static PlayerType GetAIType(string voice)
        {
            if (_aiRoles.TryGetValue(voice, out var role)) return role.Type;
            return voice.Contains("guard", StringComparison.OrdinalIgnoreCase)
                ? PlayerType.AIGuard
                : PlayerType.AIScav;
        }

        // -- Lazy name/type resolver ------------------------------------------------------
        // Called from UpdateExistingPlayers for observed (non-local) players whose
        // nickname or side read transiently failed at discovery time (heavy respawn churn).
        // Retries up to 3 times with exponential back-off then gives up.
        private const int NameResolveMaxRetries = 3;

        private static void ScheduleNameResolvRetry(Player p, long nowTick)
        {
            int streak = Math.Min(++p.NameResolveFailStreak, NameResolveMaxRetries);
            long delayMs = 200L << streak; // 400, 800, 1600 ms
            p.NextNameResolveTick = nowTick + delayMs;
        }

        internal void TryLazyResolveNameAndType(Player p, long nowTick)
        {
            // Only retry for non-local players that still have a fallback name or Default type.
            if (p.IsLocalPlayer || p.IsAI) return;
            if (p.NameResolved && p.TypeResolved) return;
            if (p.NameResolveFailStreak >= NameResolveMaxRetries) return;
            if (nowTick < p.NextNameResolveTick) return;

            bool changed = false;
            try
            {
                if (!p.NameResolved)
                {
                    if (Memory.TryReadPtr(p.Base + Offsets.ObservedPlayerView.NickName, out var nickPtr, false)
                        && nickPtr.IsValidVirtualAddress())
                    {
                        var nick = Memory.ReadUnityString(nickPtr, 64, false);
                        if (!string.IsNullOrWhiteSpace(nick))
                        {
                            p.Name = nick;
                            p.NameResolved = true;
                            changed = true;
                        }
                    }
                }

                if (!p.TypeResolved)
                {
                    int sideRaw = Memory.ReadValue<int>(p.Base + Offsets.ObservedPlayerView.Side, false);
                    if (sideRaw != 0)
                    {
                        var newType = FactionFromSide(sideRaw);
                        // Re-evaluate teammate classification with match-locked team.
                        int localTeam = _matchLocalTeamId >= 0
                            ? _matchLocalTeamId
                            : (LocalPlayer?.TeamID ?? -1);
                        if (p.TeamID >= 0 && localTeam >= 0 && p.TeamID == localTeam)
                            newType = PlayerType.Teammate;
                        p.Type = newType;
                        p.TypeResolved = true;
                        changed = true;
                    }
                }

                if (!changed)
                    ScheduleNameResolvRetry(p, nowTick);
            }
            catch { ScheduleNameResolvRetry(p, nowTick); }
        }
    }
}