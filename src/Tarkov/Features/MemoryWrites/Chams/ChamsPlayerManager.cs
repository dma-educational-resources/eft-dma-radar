using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Common.Unity.LowLevel;
using eft_dma_radar.Common.Unity.LowLevel.Types;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Chams;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// (Safe to include even if you have global usings)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;

namespace eft_dma_radar.Tarkov.Features
{
    /// <summary>
    /// Manages player chams application, caching, and restoration
    /// </summary>
    public static class PlayerChamsManager
    {
        private static readonly ConcurrentDictionary<ulong, PlayerChamsState> _playerStates = new();
        private static readonly ConcurrentDictionary<ulong, CachedPlayerMaterials> _cachedMaterials = new();
        private static readonly ConcurrentDictionary<ulong, DateTime> _playerDeathTimes = new();

        public static event Action MaterialsUpdated;

        private static Config Config => Program.Config;
        private static ChamsConfig ChamsConfig => Config.ChamsConfig;

        // Toggle: Config.Debug?.PlayerChams == true
        // If you don¡¯t have Config.Debug, just hard-force this to true temporarily.
        private static bool DebugChams = false;

        // Avoid log spam from huge renderer sets
        private const int LogSampleLimit = 12;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DLog(string msg)
        {
            if (DebugChams)
                XMLogging.WriteLine("[Player Chams DEBUG] " + msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string Hex(ulong v) => $"0x{v:X}";

        #region Public API

        public static void ProcessPlayerChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            try
            {
                DLog($"ProcessPlayerChams | Enabled={ChamsConfig.Enabled} Players={game?.Players?.Count}");

                if (!ChamsConfig.Enabled)
                {
                    DLog("Chams disabled -> RevertAllPlayerChams");
                    RevertAllPlayerChams(writes, game);
                    return;
                }

                var activePlayers = game.Players.Where(x => x.IsHostileActive || x.Type == Player.PlayerType.Teammate).ToList();
                DLog($"ActivePlayers={activePlayers.Count}");

                if (!activePlayers.Any())
                    return;

                foreach (var player in activePlayers)
                {
                    DLog($"PlayerLoop | {player.Name} Base={Hex(player.Base)} Type={player.Type} Active={player.IsActive} Alive={player.IsAlive} HostileActive={player.IsHostileActive} AimbotLocked={player.IsAimbotLocked}");

                    if (!ShouldProcessPlayer(player))
                    {
                        DLog($"Skip ShouldProcessPlayer=false | {player.Name}");
                        continue;
                    }

                    var entityType = GetEntityType(player);
                    var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                    DLog($"EntitySettings | {player.Name} EntityType={entityType} Enabled={entitySettings.Enabled} ClothingEnabled={entitySettings.ClothingChamsEnabled} GearEnabled={entitySettings.GearChamsEnabled} ClothingMode={entitySettings.ClothingChamsMode} GearMode={entitySettings.GearChamsMode}");

                    if (!entitySettings.Enabled || !AreMaterialsReady(entitySettings, entityType))
                    {
                        DLog($"Skip materials/settings not ready | {player.Name} EntityType={entityType}");
                        continue;
                    }

                    ApplyChamsToPlayer(writes, game, player, entitySettings);
                }

                ProcessDeathReverts(writes, game);
                CleanupInactivePlayers(game.Players.Select(p => p.Base).ToHashSet());
                SaveCache();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Error processing: {ex}");
            }
        }

        public static void ApplyAimbotChams(Player player, LocalGameWorld game)
        {
            if (!ChamsConfig.Enabled || player == null || !player.IsActive || !player.IsAlive)
            {
                DLog($"ApplyAimbotChams early return | Enabled={ChamsConfig.Enabled} playerNull={player == null} Active={player?.IsActive} Alive={player?.IsAlive}");
                return;
            }

            try
            {
                var state = GetOrCreateState(player.Base);
                DLog($"ApplyAimbotChams | {player.Name} Base={Hex(player.Base)} state.IsAimbotTarget={state.IsAimbotTarget} state.IsActive={state.IsActive}");

                if (state.IsAimbotTarget && state.IsActive)
                {
                    DLog("Already aimbot target + active -> return");
                    return;
                }

                var aimbotSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.AimbotTarget);

                var clothingMaterialId = GetMaterialId(aimbotSettings.ClothingChamsMode, game.CameraManager, ChamsEntityType.AimbotTarget, aimbotSettings);
                var gearMaterialId = GetMaterialId(aimbotSettings.GearChamsMode, game.CameraManager, ChamsEntityType.AimbotTarget, aimbotSettings);

                DLog($"Aimbot material IDs | clothing={clothingMaterialId} gear={gearMaterialId}");

                var primaryMaterialId = clothingMaterialId != -1 ? clothingMaterialId : gearMaterialId;
                var secondaryMaterialId = gearMaterialId != -1 ? gearMaterialId : clothingMaterialId;

                if (primaryMaterialId == -1 && secondaryMaterialId == -1)
                {
                    DLog("Aimbot both IDs == -1 -> return");
                    return;
                }

                CachePlayerMaterials(player);

                var writeHandle = new ScatterWriteHandle();

                DLog("ApplyChamsInternal (aimbot) begin");
                ApplyChamsInternal(writeHandle, player, aimbotSettings, clothingMaterialId, gearMaterialId);

                DLog("Scatter Execute (aimbot) begin");
                writeHandle.Execute(() =>
                {
                    var ok = ValidateWrite(player, game);
                    DLog($"Scatter Execute (aimbot) ValidateWrite={ok}");
                    return ok;
                });

                UpdateStateForAimbot(player, aimbotSettings.ClothingChamsMode, aimbotSettings.GearChamsMode, clothingMaterialId, gearMaterialId);

                //XMLogging.WriteLine($"[Player Chams] Applied aimbot chams (Clothing: {aimbotSettings.ClothingChamsMode}, Gear: {aimbotSettings.GearChamsMode}) to {player.Name}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to apply aimbot chams to {player.Name}: {ex}");
            }
        }

        public static void RemoveAimbotChams(Player player, LocalGameWorld game, bool revertToNormalChams = true)
        {
            if (!ChamsConfig.Enabled || player == null || !player.IsActive || !player.IsAlive)
            {
                DLog($"RemoveAimbotChams early return | Enabled={ChamsConfig.Enabled} playerNull={player == null} Active={player?.IsActive} Alive={player?.IsAlive}");
                return;
            }

            try
            {
                var state = GetState(player.Base);
                DLog($"RemoveAimbotChams | {player.Name} Base={Hex(player.Base)} stateNull={state == null} state.IsAimbotTarget={state?.IsAimbotTarget}");

                if (state == null || !state.IsAimbotTarget)
                    return;

                state.IsAimbotTarget = false;
                state.IsActive = false;

                //XMLogging.WriteLine($"[Player Chams] Removed aimbot chams from {player.Name}");

                if (revertToNormalChams)
                {
                    var entityType = GetEntityType(player);
                    var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                    DLog($"RevertToNormalChams | entityType={entityType} entityEnabled={entitySettings.Enabled}");

                    if (entitySettings.Enabled)
                    {
                        var writeHandle = new ScatterWriteHandle();
                        ApplyChamsToPlayer(writeHandle, game, player, entitySettings);
                    }
                    else
                    {
                        RevertPlayerChams(player.Base, game);
                    }
                }
                else
                {
                    RevertPlayerChams(player.Base, game);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to remove aimbot chams from {player.Name}: {ex}");
            }
        }

        public static void ApplyDeathMaterial(Player player, LocalGameWorld game)
        {
            try
            {
                if (!ChamsConfig.Enabled)
                {
                    DLog("ApplyDeathMaterial: chams disabled -> return");
                    return;
                }

                var state = GetState(player.Base);
                if (state?.HasDeathMaterialApplied == true)
                {
                    DLog($"ApplyDeathMaterial: already applied | {player.Name}");
                    return;
                }

                var entityType = GetEntityType(player);
                var entitySettings = ChamsConfig.GetEntitySettings(entityType);

                DLog($"ApplyDeathMaterial | {player.Name} EntityType={entityType} DeathEnabled={entitySettings.DeathMaterialEnabled} Mode={entitySettings.DeathMaterialMode}");

                if (!entitySettings.DeathMaterialEnabled)
                    return;

                var deathMaterialId = GetMaterialId(entitySettings.DeathMaterialMode, game.CameraManager, entityType, entitySettings);
                DLog($"DeathMaterialId={deathMaterialId}");

                if (deathMaterialId == -1)
                    return;

                var writeHandle = new ScatterWriteHandle();
                ApplyChamsInternal(writeHandle, player, entitySettings.DeathMaterialMode, deathMaterialId, deathMaterialId);

                writeHandle.Execute(() =>
                {
                    var ok = game.IsSafeToWriteMem;
                    DLog($"Scatter Execute (death) game.IsSafeToWriteMem={ok}");
                    return ok;
                });

                if (state != null)
                {
                    state.HasDeathMaterialApplied = true;
                    state.IsActive = false;
                }

                //XMLogging.WriteLine($"[Player Chams] Applied death material to {player.Name}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to apply death material to {player.Name}: {ex}");
            }
        }

        public static void RevertAllPlayerChams(ScatterWriteHandle writes, LocalGameWorld game)
        {
            DLog($"RevertAllPlayerChams | trackedStates={_playerStates.Count} cached={_cachedMaterials.Count}");

            var playersToRevert = _playerStates.Keys.ToList();
            foreach (var playerBase in playersToRevert)
            {
                RevertPlayerChams(playerBase, game);
            }

            Reset();
        }

        public static void Reset()
        {
            DLog("Reset()");

            ChamsManager.MaterialsUpdated -= OnMaterialsUpdated;

            _playerStates.Clear();
            _cachedMaterials.Clear();
            _playerDeathTimes.Clear();
        }

        public static void Initialize()
        {
            DLog("Initialize() begin");

            LoadCache();
            ApplyConfiguredColors();

            ChamsManager.MaterialsUpdated += OnMaterialsUpdated;

            XMLogging.WriteLine("[Player Chams] Manager initialized");
        }

        private static void OnMaterialsUpdated()
        {
            DLog("OnMaterialsUpdated event fired");

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    ApplyConfiguredColors();
                    XMLogging.WriteLine("[Player Chams] Applied colors after materials update");
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Player Chams] Error applying colors after materials update: {ex}");
                }
            });
        }

        #endregion

        #region Private Implementation

        private static bool ShouldProcessPlayer(Player player)
        {
            if (player.IsAimbotLocked || !player.IsActive || !player.IsAlive)
            {
                DLog($"ShouldProcessPlayer=false | {player.Name} locked={player.IsAimbotLocked} active={player.IsActive} alive={player.IsAlive}");
                return false;
            }

            var state = GetState(player.Base);
            var ok = state?.IsAimbotTarget != true || !state.IsActive;

            if (!ok)
                DLog($"ShouldProcessPlayer=false | {player.Name} (aimbot target + active)");

            return ok;
        }

        private static bool AreMaterialsReady(ChamsConfig.EntityChamsSettings entitySettings, ChamsEntityType entityType)
        {
            var clothingReady = !entitySettings.ClothingChamsEnabled ||
                IsBasicMode(entitySettings.ClothingChamsMode) ||
                ChamsManager.AreMaterialsReadyForEntityType(entityType);

            var gearReady = !entitySettings.GearChamsEnabled ||
                IsBasicMode(entitySettings.GearChamsMode) ||
                ChamsManager.AreMaterialsReadyForEntityType(entityType);

            DLog($"AreMaterialsReady | {entityType} clothingReady={clothingReady} gearReady={gearReady}");
            return clothingReady && gearReady;
        }

        private static bool IsBasicMode(ChamsMode mode) => mode == ChamsMode.Basic || mode == ChamsMode.Visible;

        private static void ApplyChamsToPlayer(ScatterWriteHandle writes, LocalGameWorld game, Player player, ChamsConfig.EntityChamsSettings entitySettings)
        {
            try
            {
                var state = GetOrCreateState(player.Base);

                var clothingMaterialId = GetClothingMaterialId(entitySettings, game.CameraManager, player);
                var gearMaterialId = GetGearMaterialId(entitySettings, game.CameraManager, player);

                bool needsUpdate = StateNeedsUpdate(state, entitySettings, clothingMaterialId, gearMaterialId);

                DLog($"ApplyChamsToPlayer | {player.Name} needsUpdate={needsUpdate} clothingId={clothingMaterialId} gearId={gearMaterialId}");

                if (!needsUpdate)
                    return;

                CachePlayerMaterials(player);

                writes.Clear();
                DLog($"ApplyChamsInternal begin | {player.Name}");
                ApplyChamsInternal(writes, player, entitySettings, clothingMaterialId, gearMaterialId);

                DLog($"Scatter Execute begin | {player.Name}");
                writes.Execute(() =>
                {
                    var ok = ValidateWrite(player, game);
                    DLog($"Scatter Execute ValidateWrite={ok} | {player.Name}");
                    return ok;
                });

                UpdateState(state, entitySettings, clothingMaterialId, gearMaterialId);

                //XMLogging.WriteLine($"[Player Chams] Applied chams to {player.Name} - Clothing: {entitySettings.ClothingChamsMode}, Gear: {entitySettings.GearChamsMode}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to apply chams to {player.Name}: {ex}");
            }
        }

        private static void UpdateStateForAimbot(Player player, ChamsMode clothingMode, ChamsMode gearMode, int clothingMaterialId, int gearMaterialId)
        {
            var state = GetOrCreateState(player.Base);
            state.ClothingMode = clothingMode;
            state.GearMode = gearMode;
            state.ClothingMaterialId = clothingMaterialId;
            state.GearMaterialId = gearMaterialId;
            state.LastAppliedTime = DateTime.UtcNow;
            state.IsActive = true;
            state.IsAimbotTarget = true;

            DLog($"UpdateStateForAimbot | {player.Name} clothingMode={clothingMode} gearMode={gearMode} clothingId={clothingMaterialId} gearId={gearMaterialId}");
        }

        private static bool StateNeedsUpdate(PlayerChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            var needs =
                state.ClothingMode != entitySettings.ClothingChamsMode ||
                state.GearMode != entitySettings.GearChamsMode ||
                state.ClothingMaterialId != clothingMaterialId ||
                state.GearMaterialId != gearMaterialId;

            if (needs)
            {
                DLog($"StateNeedsUpdate=true | clothingMode {state.ClothingMode}->{entitySettings.ClothingChamsMode} " +
                     $"gearMode {state.GearMode}->{entitySettings.GearChamsMode} " +
                     $"clothingId {state.ClothingMaterialId}->{clothingMaterialId} " +
                     $"gearId {state.GearMaterialId}->{gearMaterialId}");
            }

            return needs;
        }

        private static void ApplyChamsInternal(ScatterWriteHandle writes, Player player, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            DLog($"ApplyChamsInternal(settings) | {player.Name} clothingEnabled={entitySettings.ClothingChamsEnabled} gearEnabled={entitySettings.GearChamsEnabled} clothingId={clothingMaterialId} gearId={gearMaterialId}");

            if (entitySettings.ClothingChamsEnabled && clothingMaterialId != -1)
                ApplyClothingChams(writes, player, clothingMaterialId);
            else
                DLog($"Skip clothing apply | enabled={entitySettings.ClothingChamsEnabled} id={clothingMaterialId}");

            if (entitySettings.GearChamsEnabled && gearMaterialId != -1)
                ApplyGearChams(writes, player, gearMaterialId);
            else
                DLog($"Skip gear apply | enabled={entitySettings.GearChamsEnabled} id={gearMaterialId}");
        }

        private static void ApplyChamsInternal(ScatterWriteHandle writes, Player player, ChamsMode mode, int clothingMaterialId, int gearMaterialId)
        {
            DLog($"ApplyChamsInternal(mode) | {player.Name} mode={mode} clothingId={clothingMaterialId} gearId={gearMaterialId}");

            ApplyClothingChams(writes, player, clothingMaterialId);
            ApplyGearChams(writes, player, gearMaterialId);
        }

        private static void ApplyClothingChams(ScatterWriteHandle writes, Player player, int materialId)
        {
            DLog($"ApplyClothingChams | {player.Name} Body={Hex(player.Body)} materialId={materialId}");

            var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
            DLog($"BodyRenderers ptr={Hex(pRendererContainersArray)} offset={Hex(Offsets.PlayerBody._bodyRenderers)}");

            using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);
            DLog($"BodyRendererContainers count={rendererContainersArray.Count}");

            int containerIndex = 0;
            int rendererWrites = 0;

            foreach (var rendererContainer in rendererContainersArray)
            {
                using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                if (containerIndex < LogSampleLimit)
                    DLog($"Container[{containerIndex}] RenderersPtr={Hex(rendererContainer.Renderers)} RenderersCount={renderersArray.Count}");

                int rendererIndex = 0;
                foreach (var skinnedMeshRenderer in renderersArray)
                {
                    var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);

                    if (containerIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                        DLog($"  SMR[{containerIndex}:{rendererIndex}] smr={Hex(skinnedMeshRenderer)} renderer={Hex(renderer)}");

                    WriteMaterialToRenderer(writes, renderer, materialId);
                    rendererWrites++;
                    rendererIndex++;
                }

                containerIndex++;
            }

            DLog($"ApplyClothingChams done | {player.Name} queuedWritesToRenderers={rendererWrites}");
        }

        private static void ApplyGearChams(ScatterWriteHandle writes, Player player, int materialId)
        {
            DLog($"ApplyGearChams | {player.Name} Body={Hex(player.Body)} materialId={materialId}");

            var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
            DLog($"SlotViews ptr={Hex(slotViews)} offset={Hex(Offsets.PlayerBody.SlotViews)}");

            if (!Utils.IsValidVirtualAddress(slotViews))
            {
                DLog("SlotViews invalid -> return");
                return;
            }

            var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
            DLog($"SlotViewsDict ptr={Hex(pSlotViewsDict)} offset={Hex(Offsets.SlotViewsContainer.Dict)}");

            if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
            {
                DLog("SlotViewsDict invalid -> return");
                return;
            }

            using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

            int slotIdx = 0;
            foreach (var slot in slotViewsDict)
            {
                if (!Utils.IsValidVirtualAddress(slot.Value))
                {
                    if (slotIdx < LogSampleLimit)
                        DLog($"Slot[{slotIdx}] invalid value ptr={Hex(slot.Value)} key={Hex(slot.Key)}");
                    slotIdx++;
                    continue;
                }

                if (slotIdx < LogSampleLimit)
                    DLog($"Slot[{slotIdx}] key={Hex(slot.Key)} value={Hex(slot.Value)}");

                ProcessSlotDresses(writes, slot.Value, materialId);
                slotIdx++;
            }

            DLog($"ApplyGearChams done | {player.Name} slotsVisited={slotIdx}");
        }

        private static void ProcessSlotDresses(ScatterWriteHandle writes, ulong slotValue, int materialId)
        {
            DLog($"ProcessSlotDresses | slotValue={Hex(slotValue)} materialId={materialId}");

            var pDressesArray = Memory.ReadValue<ulong>(slotValue + Offsets.PlayerBodySubclass.Dresses);
            DLog($"Dresses ptr={Hex(pDressesArray)} offset={Hex(Offsets.PlayerBodySubclass.Dresses)}");

            if (!Utils.IsValidVirtualAddress(pDressesArray))
            {
                DLog("Dresses invalid -> return");
                return;
            }

            using var dressesArray = MemArray<ulong>.Get(pDressesArray);
            DLog($"Dresses count={dressesArray.Count}");

            int dressIndex = 0;
            foreach (var dress in dressesArray)
            {
                if (!Utils.IsValidVirtualAddress(dress))
                {
                    if (dressIndex < LogSampleLimit)
                        DLog($"Dress[{dressIndex}] invalid ptr={Hex(dress)}");
                    dressIndex++;
                    continue;
                }

                var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);

                if (dressIndex < LogSampleLimit)
                    DLog($"Dress[{dressIndex}] ptr={Hex(dress)} RenderersPtr={Hex(pRenderersArray)} offset={Hex(Offsets.Dress.Renderers)}");

                if (!Utils.IsValidVirtualAddress(pRenderersArray))
                {
                    dressIndex++;
                    continue;
                }

                using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                if (dressIndex < LogSampleLimit)
                    DLog($"Dress[{dressIndex}] RenderersCount={renderersArray.Count}");

                int rendererIndex = 0;
                foreach (var renderer in renderersArray)
                {
                    if (!Utils.IsValidVirtualAddress(renderer))
                    {
                        if (dressIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                            DLog($"  Renderer[{dressIndex}:{rendererIndex}] invalid managed={Hex(renderer)}");
                        rendererIndex++;
                        continue;
                    }

                    var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);

                    if (dressIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                        DLog($"  Renderer[{dressIndex}:{rendererIndex}] managed={Hex(renderer)} native={Hex(rendererNative)}");

                    if (Utils.IsValidVirtualAddress(rendererNative))
                        WriteMaterialToRenderer(writes, rendererNative, materialId);

                    rendererIndex++;
                }

                dressIndex++;
            }
        }

        private static void WriteMaterialToRenderer(ScatterWriteHandle writes, ulong renderer, int materialId)
        {
            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);

                if (materialsCount <= 0 || materialsCount > 30)
                {
                    DLog($"WriteMaterialToRenderer skip | renderer={Hex(renderer)} count={materialsCount}");
                    return;
                }

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);

                DLog($"WriteMaterialToRenderer | renderer={Hex(renderer)} count={materialsCount} matsPtr={Hex(materialsArrayPtr)} matId={materialId}");

                materialsArrayPtr.ThrowIfInvalidVirtualAddress();

                var materials = Enumerable.Repeat(materialId, materialsCount).ToArray();
                writes.AddBufferEntry(materialsArrayPtr, materials.AsSpan());

                DLog($"AddBufferEntry queued | matsPtr={Hex(materialsArrayPtr)} ints={materialsCount}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to write material to renderer: {ex}");
            }
        }

        private static bool ValidateWrite(Player player, LocalGameWorld game)
        {
            var corpse = Memory.ReadValue<ulong>(player.CorpseAddr, false);
            var ok = corpse == 0 && game.IsSafeToWriteMem;

            DLog($"ValidateWrite | {player.Name} CorpseAddr={Hex(player.CorpseAddr)} corpseVal={Hex(corpse)} gameSafe={game.IsSafeToWriteMem} => {ok}");
            return ok;
        }

        #endregion

        #region Material Management

        private static int GetClothingMaterialId(ChamsConfig.EntityChamsSettings entitySettings, CameraManager cameraManager, Player player)
        {
            if (!entitySettings.ClothingChamsEnabled)
            {
                DLog($"GetClothingMaterialId | {player.Name} clothing disabled");
                return -1;
            }

            var entityType = GetEntityType(player);
            var id = GetMaterialId(entitySettings.ClothingChamsMode, cameraManager, entityType, entitySettings);

            DLog($"GetClothingMaterialId | {player.Name} mode={entitySettings.ClothingChamsMode} entity={entityType} => {id}");
            return id;
        }

        private static int GetGearMaterialId(ChamsConfig.EntityChamsSettings entitySettings, CameraManager cameraManager, Player player)
        {
            if (!entitySettings.GearChamsEnabled)
            {
                DLog($"GetGearMaterialId | {player.Name} gear disabled");
                return -1;
            }

            var entityType = GetEntityType(player);
            var id = GetMaterialId(entitySettings.GearChamsMode, cameraManager, entityType, entitySettings);

            DLog($"GetGearMaterialId | {player.Name} mode={entitySettings.GearChamsMode} entity={entityType} => {id}");
            return id;
        }

        private static int GetMaterialId(ChamsMode mode, CameraManager cameraManager, ChamsEntityType entityType, ChamsConfig.EntityChamsSettings settings)
        {
            int id = mode switch
            {
                ChamsMode.Basic => GetBasicMaterialId(cameraManager),
                ChamsMode.Visible => GetVisibleMaterialId(cameraManager, settings),
                _ => ChamsManager.GetMaterialIDForPlayer(mode, entityType)
            };

            DLog($"GetMaterialId | mode={mode} entityType={entityType} => {id}");
            return id;
        }

        private static int GetBasicMaterialId(CameraManager cameraManager)
        {
            try
            {
                var ssaa = GameObjectManager.GetComponentFromBehaviour(cameraManager.FPSCamera, "SSAA");
                DLog($"GetBasicMaterialId | FPSCamera={Hex(cameraManager.FPSCamera)} SSAA={Hex(ssaa)}");

                if (ssaa == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(ssaa + 0x98);
                DLog($"GetBasicMaterialId | OpticMaskMaterial={Hex(opticMaskMaterial)} offset={Hex(0xA0)}");

                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                DLog($"GetBasicMaterialId | MonoBehaviourPtr={Hex(opticMonoBehaviour)} offset={Hex(ObjectClass.MonoBehaviourOffset)}");

                if (opticMonoBehaviour == 0) return -1;

                var id = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
                DLog($"GetBasicMaterialId | InstanceID={id}");

                return id;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to get basic material ID: {ex}");
                return -1;
            }
        }        
        private static int GetVisibleMaterialId(CameraManager cameraManager, ChamsConfig.EntityChamsSettings settings)
        {
            try
            {
                var nvgComponent = GameObjectManager.GetComponentFromBehaviour(
                    cameraManager.FPSCamera, "NightVision");

                DLog($"GetVisibleMaterialId | FPSCamera={Hex(cameraManager.FPSCamera)} NightVision={Hex(nvgComponent)}");

                if (nvgComponent == 0)
                    return -1;

                var opticMaskMaterial = Memory.ReadPtr(nvgComponent + 0xC8);
                DLog($"GetVisibleMaterialId | OpticMaskMaterial={Hex(opticMaskMaterial)} nvg+0xC8");

                if (opticMaskMaterial == 0)
                    return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                DLog($"GetVisibleMaterialId | MonoBehaviourPtr={Hex(opticMonoBehaviour)} offset={Hex(ObjectClass.MonoBehaviourOffset)}");

                if (opticMonoBehaviour == 0)
                    return -1;

                var materialId = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
                DLog($"GetVisibleMaterialId | InstanceID={materialId}");

                if (!settings.MaterialColors.TryGetValue(ChamsMode.VisCheckFlat, out var flatColors))
                    return materialId;
                var materialColorSettings = Config.ChamsConfig.GetEntitySettings(ChamsEntityType.All);
                var visibleColorString = materialColorSettings.VisibleColor;

                if (materialId != -1)
                {
                    var colorAddr = nvgComponent + 0x4C;
                    var unityColor = new UnityColor(visibleColorString);

                    Memory.WriteValue(colorAddr, unityColor);

                    DLog($"GetVisibleMaterialId | wrote color {visibleColorString} to {Hex(colorAddr)} (nvg+0x4C)");
                }

                return materialId;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to get visible material ID: {ex}");
                return -1;
            }
        }

        #endregion

        #region Color Management

        public static void ApplyConfiguredColors()
        {
            try
            {
                if (!ChamsConfig.Enabled || ChamsManager.Materials.Count == 0)
                {
                    XMLogging.WriteLine("[Player Chams] Skipping color application - chams disabled or no materials loaded");
                    return;
                }

                XMLogging.WriteLine("[Player Chams] Applying configured colors to materials...");

                using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);
                var colorsApplied = 0;

                foreach (var entityKvp in ChamsConfig.EntityChams)
                {
                    var entityType = entityKvp.Key;
                    var entitySettings = entityKvp.Value;

                    if (!ChamsManager.IsPlayerEntityType(entityType))
                        continue;

                    foreach (var materialKvp in ChamsManager.Materials)
                    {
                        var (mode, matEntityType) = materialKvp.Key;
                        var material = materialKvp.Value;

                        if (matEntityType != entityType || material.InstanceID == 0)
                            continue;

                        try
                        {
                            SKColor visibleColor, invisibleColor;
                            var materialColorSettings = entitySettings.MaterialColors?.ContainsKey(mode) == true
                                ? entitySettings.MaterialColors[mode]
                                : null;

                            if (materialColorSettings != null)
                            {
                                if (!SKColor.TryParse(materialColorSettings.VisibleColor, out visibleColor))
                                    visibleColor = SKColor.Parse("#00FF00");

                                if (!SKColor.TryParse(materialColorSettings.InvisibleColor, out invisibleColor))
                                    invisibleColor = SKColor.Parse("#FF0000");
                            }
                            else
                            {
                                if (!SKColor.TryParse(entitySettings.VisibleColor, out visibleColor))
                                    visibleColor = SKColor.Parse("#00FF00");

                                if (!SKColor.TryParse(entitySettings.InvisibleColor, out invisibleColor))
                                    invisibleColor = SKColor.Parse("#FF0000");
                            }

                            var visibleUnityColor = new UnityColor(visibleColor.Red, visibleColor.Green, visibleColor.Blue, visibleColor.Alpha);
                            var invisibleUnityColor = new UnityColor(invisibleColor.Red, invisibleColor.Green, invisibleColor.Blue, invisibleColor.Alpha);

                            //NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorVisible, visibleUnityColor);
                            //NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorInvisible, invisibleUnityColor);

                            colorsApplied++;
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[Player Chams] Failed to set color for {mode}/{entityType}: {ex}");
                        }
                    }
                }

                XMLogging.WriteLine($"[Player Chams] Applied colors to {colorsApplied} materials");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to apply configured colors: {ex}");
            }
        }

        #endregion

        #region State Management

        private static PlayerChamsState GetOrCreateState(ulong playerBase)
        {
            var created = !_playerStates.ContainsKey(playerBase);
            var state = _playerStates.GetOrAdd(playerBase, _ => new PlayerChamsState());

            if (created)
                DLog($"GetOrCreateState created | playerBase={Hex(playerBase)} tracked={_playerStates.Count}");

            return state;
        }

        private static PlayerChamsState GetState(ulong playerBase)
        {
            return _playerStates.TryGetValue(playerBase, out var state) ? state : null;
        }

        private static void UpdateState(PlayerChamsState state, ChamsConfig.EntityChamsSettings entitySettings, int clothingMaterialId, int gearMaterialId)
        {
            state.ClothingMode = entitySettings.ClothingChamsMode;
            state.GearMode = entitySettings.GearChamsMode;
            state.ClothingMaterialId = clothingMaterialId;
            state.GearMaterialId = gearMaterialId;
            state.LastAppliedTime = DateTime.UtcNow;
            state.IsActive = true;
            state.IsAimbotTarget = false;

            DLog($"UpdateState | clothingMode={state.ClothingMode} gearMode={state.GearMode} clothingId={state.ClothingMaterialId} gearId={state.GearMaterialId} active={state.IsActive}");
        }

        #endregion

        #region Death and Cleanup

        private static void ProcessDeathReverts(ScatterWriteHandle writes, LocalGameWorld game)
        {
            var currentTime = DateTime.UtcNow;
            var playersToRevert = new List<ulong>();

            foreach (var kvp in _playerStates)
            {
                var playerBase = kvp.Key;
                var state = kvp.Value;

                if (!state.IsActive)
                    continue;

                var player = game.Players.FirstOrDefault(p => p.Base == playerBase);
                if (player == null || !player.IsAlive)
                {
                    if (!_playerDeathTimes.ContainsKey(playerBase))
                        _playerDeathTimes[playerBase] = currentTime;

                    DLog($"DeathCheck | playerBase={Hex(playerBase)} playerNull={player == null} alive={player?.IsAlive}");

                    var entityType = GetEntityTypeFromBase(playerBase, game);
                    if (entityType.HasValue)
                    {
                        var entitySettings = ChamsConfig.GetEntitySettings(entityType.Value);

                        if (entitySettings.RevertOnDeath && !entitySettings.DeathMaterialEnabled)
                        {
                            DLog($"Queue revert on death | playerBase={Hex(playerBase)} entity={entityType.Value}");
                            playersToRevert.Add(playerBase);
                        }
                        else if (entitySettings.DeathMaterialEnabled)
                        {
                            state.IsActive = false;
                            //XMLogging.WriteLine($"[Player Chams] Player {playerBase:X} died - keeping death material applied");
                        }
                    }
                }
                else
                {
                    _playerDeathTimes.TryRemove(playerBase, out _);
                }
            }

            foreach (var playerBase in playersToRevert)
                RevertPlayerChams(playerBase, game);
        }

        private static void RevertPlayerChams(ulong playerBase, LocalGameWorld game = null)
        {
            try
            {
                if (!_playerStates.TryGetValue(playerBase, out var state))
                    return;

                DLog($"RevertPlayerChams | playerBase={Hex(playerBase)} cached={_cachedMaterials.ContainsKey(playerBase)}");

                if (_cachedMaterials.TryGetValue(playerBase, out var cached))
                    RestorePlayerMaterials(playerBase, cached, game);

                state.IsActive = false;
                _playerDeathTimes.TryRemove(playerBase, out _);

                //XMLogging.WriteLine($"[Player Chams] Reverted chams for player {playerBase:X}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to revert chams for player {playerBase:X}: {ex}");
            }
        }

        private static void RestorePlayerMaterials(ulong playerBase, CachedPlayerMaterials cached, LocalGameWorld game)
        {
            try
            {
                var player = game?.Players.FirstOrDefault(p => p.Base == playerBase);
                if (player == null)
                {
                    DLog($"RestorePlayerMaterials: player not found in game list | base={Hex(playerBase)}");
                    return;
                }

                DLog($"RestorePlayerMaterials | {cached.PlayerName} clothingKeys={cached.ClothingMaterials?.Count} gearKeys={cached.GearMaterials?.Count}");

                RestoreClothingMaterials(player, cached.ClothingMaterials);
                RestoreGearMaterials(player, cached.GearMaterials);

                //XMLogging.WriteLine($"[Player Chams] Restored materials for {cached.PlayerName}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to restore materials for player {playerBase:X}: {ex}");
            }
        }

        private static void RestoreClothingMaterials(Player player, Dictionary<string, Dictionary<int, int>> clothingMaterials)
        {
            try
            {
                var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
                using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);

                DLog($"RestoreClothingMaterials | {player.Name} containers={rendererContainersArray.Count} keys={clothingMaterials?.Count}");

                for (var containerIndex = 0; containerIndex < rendererContainersArray.Count; containerIndex++)
                {
                    var rendererContainer = rendererContainersArray[containerIndex];
                    using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                    for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                    {
                        var key = $"clothing_{containerIndex}_{rendererIndex}";
                        if (clothingMaterials.TryGetValue(key, out var materials))
                        {
                            var skinnedMeshRenderer = renderersArray[rendererIndex];
                            var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);
                            RestoreRendererMaterials(renderer, materials);

                            if (containerIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                                DLog($"Restore clothing key={key} renderer={Hex(renderer)} mats={materials.Count}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to restore clothing materials: {ex}");
            }
        }

        private static void RestoreGearMaterials(Player player, Dictionary<string, Dictionary<int, int>> gearMaterials)
        {
            try
            {
                var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
                if (!Utils.IsValidVirtualAddress(slotViews))
                    return;

                var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
                if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
                    return;

                using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

                DLog($"RestoreGearMaterials | {player.Name} keys={gearMaterials?.Count}");

                var slotIndex = 0;
                foreach (var slot in slotViewsDict)
                {
                    if (!Utils.IsValidVirtualAddress(slot.Value))
                        continue;

                    var pDressesArray = Memory.ReadValue<ulong>(slot.Value + Offsets.PlayerBodySubclass.Dresses);
                    if (!Utils.IsValidVirtualAddress(pDressesArray))
                        continue;

                    using var dressesArray = MemArray<ulong>.Get(pDressesArray);

                    for (var dressIndex = 0; dressIndex < dressesArray.Count; dressIndex++)
                    {
                        var dress = dressesArray[dressIndex];
                        if (!Utils.IsValidVirtualAddress(dress))
                            continue;

                        var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);
                        if (!Utils.IsValidVirtualAddress(pRenderersArray))
                            continue;

                        using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                        for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                        {
                            var key = $"gear_{slotIndex}_{dressIndex}_{rendererIndex}";
                            if (gearMaterials.TryGetValue(key, out var materials))
                            {
                                var renderer = renderersArray[rendererIndex];
                                if (!Utils.IsValidVirtualAddress(renderer))
                                    continue;

                                var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                                if (Utils.IsValidVirtualAddress(rendererNative))
                                {
                                    RestoreRendererMaterials(rendererNative, materials);

                                    if (slotIndex < LogSampleLimit && dressIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                                        DLog($"Restore gear key={key} native={Hex(rendererNative)} mats={materials.Count}");
                                }
                            }
                        }
                    }
                    slotIndex++;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to restore gear materials: {ex}");
            }
        }

        private static void RestoreRendererMaterials(ulong renderer, Dictionary<int, int> originalMaterials)
        {
            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                    return;

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                if (!Utils.IsValidVirtualAddress(materialsArrayPtr))
                    return;

                DLog($"RestoreRendererMaterials | renderer={Hex(renderer)} count={materialsCount} matsPtr={Hex(materialsArrayPtr)} originals={originalMaterials.Count}");

                for (int i = 0; i < materialsCount; i++)
                {
                    if (originalMaterials.TryGetValue(i, out var originalMaterial))
                        Memory.WriteValue(materialsArrayPtr + (ulong)(i * 4), originalMaterial);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to restore renderer materials: {ex}");
            }
        }

        private static void CleanupInactivePlayers(HashSet<ulong> activePlayerBases)
        {
            var inactivePlayerBases = _playerStates.Keys.Where(playerBase => !activePlayerBases.Contains(playerBase)).ToList();

            if (inactivePlayerBases.Any())
                DLog($"CleanupInactivePlayers | inactiveCount={inactivePlayerBases.Count} activeCount={activePlayerBases.Count}");

            foreach (var playerBase in inactivePlayerBases)
            {
                RevertPlayerChams(playerBase);
                _playerStates.TryRemove(playerBase, out _);
                _playerDeathTimes.TryRemove(playerBase, out _);
                _cachedMaterials.TryRemove(playerBase, out _);

                DLog($"Removed inactive playerBase={Hex(playerBase)}");
            }

            if (inactivePlayerBases.Any())
                XMLogging.WriteLine($"[Player Chams] Cleaned up {inactivePlayerBases.Count} inactive players");
        }

        #endregion

        #region Caching

        private static void CachePlayerMaterials(Player player)
        {
            try
            {
                if (_cachedMaterials.ContainsKey(player.Base))
                {
                    DLog($"CachePlayerMaterials skip (already cached) | {player.Name} base={Hex(player.Base)}");
                    return;
                }

                DLog($"CachePlayerMaterials begin | {player.Name} base={Hex(player.Base)}");

                var clothingMaterials = new Dictionary<string, Dictionary<int, int>>();
                var gearMaterials = new Dictionary<string, Dictionary<int, int>>();

                CacheClothingMaterials(player, clothingMaterials);
                CacheGearMaterials(player, gearMaterials);

                DLog($"CachePlayerMaterials collected | clothingKeys={clothingMaterials.Count} gearKeys={gearMaterials.Count}");

                if (clothingMaterials.Count > 0 || gearMaterials.Count > 0)
                {
                    _cachedMaterials[player.Base] = new CachedPlayerMaterials
                    {
                        PlayerBase = player.Base,
                        PlayerName = player.Name,
                        ClothingMaterials = clothingMaterials,
                        GearMaterials = gearMaterials,
                        CacheTime = DateTime.UtcNow
                    };

                    //XMLogging.WriteLine($"[Player Chams] Cached materials for {player.Name} - Clothing: {clothingMaterials.Count}, Gear: {gearMaterials.Count}");
                }
                else
                {
                    DLog($"CachePlayerMaterials: nothing captured | {player.Name}");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to cache materials for {player.Name}: {ex}");
            }
        }

        private static void CacheClothingMaterials(Player player, Dictionary<string, Dictionary<int, int>> clothingMaterials)
        {
            try
            {
                var pRendererContainersArray = Memory.ReadPtr(player.Body + Offsets.PlayerBody._bodyRenderers);
                using var rendererContainersArray = MemArray<Types.BodyRendererContainer>.Get(pRendererContainersArray);

                DLog($"CacheClothingMaterials | {player.Name} containers={rendererContainersArray.Count}");

                for (var containerIndex = 0; containerIndex < rendererContainersArray.Count; containerIndex++)
                {
                    var rendererContainer = rendererContainersArray[containerIndex];
                    using var renderersArray = MemArray<ulong>.Get(rendererContainer.Renderers);

                    for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                    {
                        var skinnedMeshRenderer = renderersArray[rendererIndex];
                        var renderer = Memory.ReadPtr(skinnedMeshRenderer + UnityOffsets.SkinnedMeshRenderer.Renderer);

                        var materials = CacheRendererMaterials(renderer);
                        if (materials.Count > 0)
                        {
                            var key = $"clothing_{containerIndex}_{rendererIndex}";
                            clothingMaterials[key] = materials;

                            if (containerIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                                DLog($"Cached clothing key={key} renderer={Hex(renderer)} mats={materials.Count}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to cache clothing materials: {ex}");
            }
        }

        private static void CacheGearMaterials(Player player, Dictionary<string, Dictionary<int, int>> gearMaterials)
        {
            try
            {
                var slotViews = Memory.ReadValue<ulong>(player.Body + Offsets.PlayerBody.SlotViews);
                if (!Utils.IsValidVirtualAddress(slotViews))
                {
                    DLog("CacheGearMaterials: SlotViews invalid -> return");
                    return;
                }

                var pSlotViewsDict = Memory.ReadValue<ulong>(slotViews + Offsets.SlotViewsContainer.Dict);
                if (!Utils.IsValidVirtualAddress(pSlotViewsDict))
                {
                    DLog("CacheGearMaterials: SlotViewsDict invalid -> return");
                    return;
                }

                using var slotViewsDict = MemDictionary<ulong, ulong>.Get(pSlotViewsDict);

                var slotIndex = 0;
                foreach (var slot in slotViewsDict)
                {
                    if (!Utils.IsValidVirtualAddress(slot.Value))
                        continue;

                    var pDressesArray = Memory.ReadValue<ulong>(slot.Value + Offsets.PlayerBodySubclass.Dresses);
                    if (!Utils.IsValidVirtualAddress(pDressesArray))
                        continue;

                    using var dressesArray = MemArray<ulong>.Get(pDressesArray);

                    for (var dressIndex = 0; dressIndex < dressesArray.Count; dressIndex++)
                    {
                        var dress = dressesArray[dressIndex];
                        if (!Utils.IsValidVirtualAddress(dress))
                            continue;

                        var pRenderersArray = Memory.ReadValue<ulong>(dress + Offsets.Dress.Renderers);
                        if (!Utils.IsValidVirtualAddress(pRenderersArray))
                            continue;

                        using var renderersArray = MemArray<ulong>.Get(pRenderersArray);

                        for (var rendererIndex = 0; rendererIndex < renderersArray.Count; rendererIndex++)
                        {
                            var renderer = renderersArray[rendererIndex];
                            if (!Utils.IsValidVirtualAddress(renderer))
                                continue;

                            var rendererNative = Memory.ReadValue<ulong>(renderer + 0x10);
                            if (!Utils.IsValidVirtualAddress(rendererNative))
                                continue;

                            var materials = CacheRendererMaterials(rendererNative);
                            if (materials.Count > 0)
                            {
                                var key = $"gear_{slotIndex}_{dressIndex}_{rendererIndex}";
                                gearMaterials[key] = materials;

                                if (slotIndex < LogSampleLimit && dressIndex < LogSampleLimit && rendererIndex < LogSampleLimit)
                                    DLog($"Cached gear key={key} native={Hex(rendererNative)} mats={materials.Count}");
                            }
                        }
                    }
                    slotIndex++;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to cache gear materials: {ex}");
            }
        }

        private static Dictionary<int, int> CacheRendererMaterials(ulong renderer)
        {
            var materials = new Dictionary<int, int>();

            try
            {
                int materialsCount = Memory.ReadValueEnsure<int>(renderer + UnityOffsets.Renderer.Count);
                if (materialsCount <= 0 || materialsCount > 30)
                {
                    DLog($"CacheRendererMaterials skip | renderer={Hex(renderer)} count={materialsCount}");
                    return materials;
                }

                var materialsArrayPtr = Memory.ReadValueEnsure<ulong>(renderer + UnityOffsets.Renderer.Materials);
                if (!Utils.IsValidVirtualAddress(materialsArrayPtr))
                {
                    DLog($"CacheRendererMaterials invalid matsPtr | renderer={Hex(renderer)} matsPtr={Hex(materialsArrayPtr)}");
                    return materials;
                }

                for (int i = 0; i < materialsCount; i++)
                {
                    var materialId = Memory.ReadValue<int>(materialsArrayPtr + (ulong)(i * 4));
                    materials[i] = materialId;
                }

                if (materialsCount > 0)
                {
                    var sample = string.Join(", ", materials.Take(Math.Min(materials.Count, 6)).Select(kv => $"{kv.Key}:{kv.Value}"));
                    DLog($"CacheRendererMaterials ok | renderer={Hex(renderer)} count={materialsCount} sample=[{sample}]");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to cache renderer materials: {ex}");
            }

            return materials;
        }

        private static void LoadCache()
        {
            try
            {
                var cache = Config.LowLevelCache.PlayerChamsCache;
                _cachedMaterials.Clear();

                foreach (var kvp in cache)
                    _cachedMaterials[kvp.Key] = kvp.Value;

                //XMLogging.WriteLine($"[Player Chams] Loaded {cache.Count} cached player materials");
                DLog($"LoadCache done | cached={_cachedMaterials.Count}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to load cache: {ex}");
            }
        }

        private static void SaveCache()
        {
            try
            {
                var cache = Config.LowLevelCache.PlayerChamsCache;
                cache.Clear();

                foreach (var kvp in _cachedMaterials)
                    cache[kvp.Key] = kvp.Value;

                DLog($"SaveCache queued | cached={_cachedMaterials.Count}");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Config.LowLevelCache.SaveAsync();
                        DLog("SaveCache completed");
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[Player Chams] Failed to save cache: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to prepare cache for saving: {ex}");
            }
        }

        #endregion

        #region Utilities

        private static ChamsEntityType GetEntityType(Player player)
        {
            return player.Type switch
            {
                Player.PlayerType.USEC or Player.PlayerType.BEAR or Player.PlayerType.SpecialPlayer or Player.PlayerType.Streamer => ChamsEntityType.PMC,
                Player.PlayerType.Teammate => ChamsEntityType.Teammate,
                Player.PlayerType.AIScav => ChamsEntityType.AI,
                Player.PlayerType.AIBoss => ChamsEntityType.Boss,
                Player.PlayerType.AIRaider => ChamsEntityType.Guard,
                Player.PlayerType.PScav => ChamsEntityType.PlayerScav,
                _ => ChamsEntityType.AimbotTarget,
            };
        }

        private static ChamsEntityType? GetEntityTypeFromBase(ulong playerBase, LocalGameWorld game)
        {
            var player = game.Players.FirstOrDefault(p => p.Base == playerBase);
            return player != null ? GetEntityType(player) : null;
        }

        public static string GetDiagnosticInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Player Chams Diagnostic Info]");
            sb.AppendLine($"Active Player States: {_playerStates.Count(s => s.Value.IsActive)}");
            sb.AppendLine($"Cached Player Materials: {_cachedMaterials.Count}");
            sb.AppendLine($"Player States Tracked: {_playerStates.Count}");

            var playerEntityTypes = new[] {
                ChamsEntityType.PMC, ChamsEntityType.Teammate, ChamsEntityType.AI,
                ChamsEntityType.Boss, ChamsEntityType.Guard, ChamsEntityType.PlayerScav,
                ChamsEntityType.AimbotTarget
            };

            sb.AppendLine("\nMaterial Availability:");
            foreach (var entityType in playerEntityTypes)
                sb.AppendLine($"  {ChamsManager.GetEntityTypeStatus(entityType)}");

            if (_cachedMaterials.Any())
            {
                sb.AppendLine("\nCached Players (Original Materials):");
                foreach (var kvp in _cachedMaterials.Take(10))
                {
                    var cached = kvp.Value;
                    var totalMaterials = cached.ClothingMaterials.Values.Sum(d => d.Count) +
                                       cached.GearMaterials.Values.Sum(d => d.Count);
                    sb.AppendLine($"  {cached.PlayerName}: {totalMaterials} materials (cached {cached.CacheTime:HH:mm:ss})");
                }
                if (_cachedMaterials.Count > 10)
                    sb.AppendLine($"  ... and {_cachedMaterials.Count - 10} more");
            }

            return sb.ToString();
        }

        #endregion

        #region State Class

        private class PlayerChamsState
        {
            public ChamsMode ClothingMode { get; set; } = ChamsMode.Basic;
            public ChamsMode GearMode { get; set; } = ChamsMode.Basic;
            public int ClothingMaterialId { get; set; } = -1;
            public int GearMaterialId { get; set; } = -1;
            public DateTime LastAppliedTime { get; set; }
            public bool IsActive { get; set; }
            public bool IsAimbotTarget { get; set; }
            public bool HasDeathMaterialApplied { get; set; }
        }

        #endregion
    }
}
