using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.LowLevel;
using eft_dma_radar.Common.Unity.LowLevel.Types;
using eft_dma_radar.Tarkov.Features.MemoryWrites.Chams;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;

namespace eft_dma_radar.Tarkov.Features
{
    /// <summary>
    /// Manages loot chams application, caching, and restoration
    /// </summary>
    public static class LootChamsManager
    {
        private static readonly ConcurrentDictionary<string, CachedLootMaterial> _cachedMaterials = new();
        private static readonly ConcurrentDictionary<string, ChamsMode> _activeLootChams = new();

        private static Config Config => Program.Config;
        private static ChamsConfig ChamsConfig => Config.ChamsConfig;

        #region Public API

        public static void ProcessLootChams()
        {
            try
            {
                //if (!ChamsConfig.Enabled)
                //{
                //    if (_activeLootChams.Count > 0)
                //    {
                //        NotificationsShared.Info("attempting to disable chams in ProcessLootChams");
                //        RevertAllLootChams();
                //    }
                //        
//
                //    return;
                //}
//
                //var lootManager = Memory.Loot;
                //if (lootManager?.FilteredLoot == null)
                //    return;
//
                //var currentImportantItems = new HashSet<string>();
                //var currentQuestItems = new HashSet<string>();
//
                //ProcessImportantItemChams(lootManager, currentImportantItems);
                //ProcessQuestItemChams(lootManager, currentQuestItems);
                //RevertObsoleteChams(currentImportantItems, currentQuestItems);
                //SaveCache();
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Error processing: {ex.Message}");
            }
        }

        public static void RevertAllLootChams()
        {
            var itemsToRevert = _activeLootChams.Keys.ToList();

            foreach (var itemId in itemsToRevert)
            {
                RevertItemChams(itemId);
            }

            _cachedMaterials.Clear();
            _activeLootChams.Clear();

            if (itemsToRevert.Count > 0)
                XMLogging.WriteLine($"[Loot Chams] Reverted all chams for {itemsToRevert.Count} items");
        }

        public static void Reset()
        {
            ChamsManager.MaterialsUpdated -= OnMaterialsUpdated;

            if (_activeLootChams.Count > 0)
            {
                RevertAllLootChams();
            }
            else
            {
                _cachedMaterials.Clear();
                _activeLootChams.Clear();
            }
        }

        public static void Initialize()
        {
            LoadCache();
            ApplyConfiguredColors();
            
            ChamsManager.MaterialsUpdated += OnMaterialsUpdated;

            XMLogging.WriteLine("[Loot Chams] Manager initialized");
        }

        private static void OnMaterialsUpdated()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200);
                    ApplyConfiguredColors();
                    XMLogging.WriteLine("[Loot Chams] Applied colors after materials update");
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[Loot Chams] Error applying colors after materials update: {ex.Message}");
                }
            });
        }

        public static void ApplyConfiguredColors()
        {
            try
            {
                if (!ChamsConfig.Enabled)
                    return;

                XMLogging.WriteLine("[Loot Chams] Applying configured colors to materials...");

                using var chamsColorMem = new RemoteBytes(SizeChecker<UnityColor>.Size);
                var colorsApplied = 0;

                foreach (var entityKvp in ChamsConfig.EntityChams)
                {
                    var entityType = entityKvp.Key;
                    var entitySettings = entityKvp.Value;

                    if (!ChamsManager.IsLootEntityType(entityType))
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

                            if (ApplyColorsToMaterial(chamsColorMem, material, visibleUnityColor, invisibleUnityColor))
                                colorsApplied++;
                        }
                        catch (Exception ex)
                        {
                            XMLogging.WriteLine($"[Loot Chams] Failed to set color for {mode}/{entityType}: {ex.Message}");
                        }
                    }
                }

                XMLogging.WriteLine($"[Loot Chams] Applied colors to {colorsApplied} materials");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to apply configured colors: {ex.Message}");
            }
        }

        #endregion

        #region Private Implementation

        private static void ProcessImportantItemChams(eft_dma_radar.Tarkov.Loot.LootManager lootManager, HashSet<string> currentImportantItems)
        {
            var importantItemSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.ImportantItem);
            if (!importantItemSettings.Enabled)
                return;

            // Add this check - if advanced mem writes is off, use basic mode instead of failing
            if (!Config.AdvancedMemWrites && importantItemSettings.Mode != ChamsMode.Basic && importantItemSettings.Mode != ChamsMode.Visible)
            {
                XMLogging.WriteLine($"[Loot Chams] AdvancedMemWrites disabled, using Basic mode for ImportantItem instead of {importantItemSettings.Mode}");
                var basicMaterialId = GetMaterialId(ChamsMode.Basic, ChamsEntityType.ImportantItem, importantItemSettings);
                if (basicMaterialId != -1)
                {
                    foreach (var item in lootManager.FilteredLoot)
                    {
                        if (!IsValidLootItem(item) || !ShouldApplyImportantChams(item))
                            continue;

                        currentImportantItems.Add(item.ID);
                    }
                    ApplyChamsToItems(lootManager.FilteredLoot, currentImportantItems, ChamsMode.Basic, basicMaterialId);
                }
                return;
            }

            if (!IsModeAvailable(importantItemSettings.Mode, ChamsEntityType.ImportantItem))
            {
                // Only log this if we actually need advanced materials
                if (importantItemSettings.Mode != ChamsMode.Basic && importantItemSettings.Mode != ChamsMode.Visible)
                    XMLogging.WriteLine($"[Loot Chams] Materials not ready for ImportantItem with mode {importantItemSettings.Mode}");
                return;
            }

            foreach (var item in lootManager.FilteredLoot)
            {
                if (!IsValidLootItem(item) || !ShouldApplyImportantChams(item))
                    continue;

                currentImportantItems.Add(item.ID);
            }

            var materialId = GetMaterialId(importantItemSettings.Mode, ChamsEntityType.ImportantItem, importantItemSettings);
            if (materialId != -1)
                ApplyChamsToItems(lootManager.FilteredLoot, currentImportantItems, importantItemSettings.Mode, materialId);
        }

        private static void ProcessQuestItemChams(eft_dma_radar.Tarkov.Loot.LootManager lootManager, HashSet<string> currentQuestItems)
        {
            var questItemSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.QuestItem);
            if (!questItemSettings.Enabled)
                return;

            if (!Config.AdvancedMemWrites && questItemSettings.Mode != ChamsMode.Basic && questItemSettings.Mode != ChamsMode.Visible)
            {
                XMLogging.WriteLine($"[Loot Chams] AdvancedMemWrites disabled, using Basic mode for QuestItem instead of {questItemSettings.Mode}");
                var basicMaterialId = GetMaterialId(ChamsMode.Basic, ChamsEntityType.QuestItem, questItemSettings);
                if (basicMaterialId != -1)
                {
                    foreach (var item in lootManager.FilteredLoot)
                    {
                        if (!IsValidLootItem(item) || !ShouldApplyQuestChams(item))
                            continue;

                        currentQuestItems.Add(item.ID);
                    }
                    ApplyChamsToItems(lootManager.FilteredLoot, currentQuestItems, ChamsMode.Basic, basicMaterialId);
                }
                return;
            }

            if (!IsModeAvailable(questItemSettings.Mode, ChamsEntityType.QuestItem))
            {
                if (questItemSettings.Mode != ChamsMode.Basic && questItemSettings.Mode != ChamsMode.Visible)
                    XMLogging.WriteLine($"[Loot Chams] Materials not ready for QuestItem with mode {questItemSettings.Mode}");
                return;
            }

            foreach (var item in lootManager.FilteredLoot)
            {
                if (!IsValidLootItem(item) || !ShouldApplyQuestChams(item))
                    continue;

                currentQuestItems.Add(item.ID);
            }

            var materialId = GetMaterialId(questItemSettings.Mode, ChamsEntityType.QuestItem, questItemSettings);
            if (materialId != -1)
                ApplyChamsToItems(lootManager.FilteredLoot, currentQuestItems, questItemSettings.Mode, materialId);
        }

        private static bool IsValidLootItem(LootItem item)
        {
            return item?.InteractiveClass != 0 && !string.IsNullOrEmpty(item.ID);
        }

        private static bool IsModeAvailable(ChamsMode mode, ChamsEntityType lootType)
        {
            if (mode == ChamsMode.Basic || mode == ChamsMode.Visible)
                return true;

            return ChamsManager.AreMaterialsReadyForEntityType(lootType);
        }

        private static bool ShouldApplyImportantChams(LootItem item)
        {
            return item.IsValuableLoot || item.IsImportant || item.IsWishlisted ||
                   (item.MatchedFilter != null && item.Important);
        }

        private static bool ShouldApplyQuestChams(LootItem item)
        {
            return item.IsQuestCondition || item.Name?.StartsWith("Q_") == true;
        }

        private static int GetMaterialId(ChamsMode mode, ChamsEntityType entityType, ChamsConfig.EntityChamsSettings settings)
        {
            if (Memory.Game is not LocalGameWorld game)
                return -1;

            var cm = game.CameraManager;

            return mode switch
            {
                ChamsMode.Basic => GetBasicMaterialId(cm),
                ChamsMode.Visible => GetVisibleMaterialId(cm, settings),
                _ => ChamsManager.GetMaterialIDForLoot(mode, entityType)
            };
        }

        private static int GetBasicMaterialId(CameraManager cameraManager)
        {
            try
            {
                var ssaa = GameObjectManager.GetComponentFromBehaviour(cameraManager.FPSCamera, "SSAA");
                //DLog($"GetBasicMaterialId | FPSCamera={Hex(cameraManager.FPSCamera)} SSAA={Hex(ssaa)}");

                if (ssaa == 0) return -1;

                var opticMaskMaterial = Memory.ReadPtr(ssaa + 0xA0);
                //DLog($"GetBasicMaterialId | OpticMaskMaterial={Hex(opticMaskMaterial)} offset={Hex(0xA0)}");

                if (opticMaskMaterial == 0) return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                //DLog($"GetBasicMaterialId | MonoBehaviourPtr={Hex(opticMonoBehaviour)} offset={Hex(ObjectClass.MonoBehaviourOffset)}");

                if (opticMonoBehaviour == 0) return -1;

                var id = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
                //DLog($"GetBasicMaterialId | InstanceID={id}");

                return id;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to get basic material ID: {ex}");
                return -1;
            }
        }        
        private static int GetVisible2Id(CameraManager cameraManager, ChamsConfig.EntityChamsSettings settings)
        {
            try
            {
                var nvgComponent = GameObjectManager.GetComponentFromBehaviour(cameraManager.FPSCamera, "FrostbiteEffect");

                //DLog($"GetVisibleMaterialId | FPSCamera={Hex(cameraManager.FPSCamera)} TextureMask={Hex(nvgComponent)}");

                if (nvgComponent == 0)
                    return -1;

                var opticMaskMaterial = Memory.ReadPtr(nvgComponent + 0x28);
                //DLog($"GetVisibleMaterialId | OpticMaskMaterial={Hex(opticMaskMaterial)} nvg+0xA0");

                if (opticMaskMaterial == 0)
                    return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                //DLog($"GetVisibleMaterialId | MonoBehaviourPtr={Hex(opticMonoBehaviour)} offset={Hex(ObjectClass.MonoBehaviourOffset)}");

                if (opticMonoBehaviour == 0)
                    return -1;

                var materialId = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
                //DLog($"GetVisibleMaterialId | InstanceID={materialId}");

                if (!settings.MaterialColors.TryGetValue(ChamsMode.VisCheckFlat, out var flatColors))
                    return materialId;

                var invisibleColorString = flatColors.InvisibleColor;

                if (materialId != -1)
                {
                    var colorAddr = nvgComponent + 0x38;
                    var unityColor = new UnityColor(invisibleColorString);

                    Memory.WriteValue(colorAddr, unityColor);

                    //DLog($"GetVisibleMaterialId | wrote color {invisibleColorString} to {Hex(colorAddr)} (nvg+0x4C)");
                }

                return materialId;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to get visible material ID: {ex}");
                return -1;
            }
        }

        private static int GetVisibleMaterialId(CameraManager cameraManager, ChamsConfig.EntityChamsSettings settings)
        {
            try
            {
                var nvgComponent = GameObjectManager.GetComponentFromBehaviour(
                    cameraManager.FPSCamera, "NightVision");

                //DLog($"GetVisibleMaterialId | FPSCamera={Hex(cameraManager.FPSCamera)} NightVision={Hex(nvgComponent)}");

                if (nvgComponent == 0)
                    return -1;

                var opticMaskMaterial = Memory.ReadPtr(nvgComponent + 0xC8);
                //DLog($"GetVisibleMaterialId | OpticMaskMaterial={Hex(opticMaskMaterial)} nvg+0xC8");

                if (opticMaskMaterial == 0)
                    return -1;

                var opticMonoBehaviour = Memory.ReadPtr(opticMaskMaterial + ObjectClass.MonoBehaviourOffset);
                //DLog($"GetVisibleMaterialId | MonoBehaviourPtr={Hex(opticMonoBehaviour)} offset={Hex(ObjectClass.MonoBehaviourOffset)}");

                if (opticMonoBehaviour == 0)
                    return -1;

                var materialId = Memory.ReadValue<MonoBehaviour>(opticMonoBehaviour).InstanceID;
                //DLog($"GetVisibleMaterialId | InstanceID={materialId}");

                if (!settings.MaterialColors.TryGetValue(ChamsMode.VisCheckFlat, out var flatColors))
                    return materialId;
                var materialColorSettings = Config.ChamsConfig.GetEntitySettings(ChamsEntityType.All);
                var visibleColorString = materialColorSettings.VisibleColor;

                if (materialId != -1)
                {
                    var colorAddr = nvgComponent + 0x4C;
                    var unityColor = new UnityColor(visibleColorString);

                    Memory.WriteValue(colorAddr, unityColor);

                    //DLog($"GetVisibleMaterialId | wrote color {visibleColorString} to {Hex(colorAddr)} (nvg+0x4C)");
                }

                return materialId;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Player Chams] Failed to get visible material ID: {ex}");
                return -1;
            }
        }

        private static void ApplyChamsToItems(IReadOnlyList<LootItem> allLoot, HashSet<string> targetItemIds, ChamsMode mode, int materialId)
        {
            var itemsToProcess = allLoot
                .Where(item => targetItemIds.Contains(item.ID) && item.InteractiveClass != 0)
                .GroupBy(item => item.ID)
                .ToList();

            foreach (var itemGroup in itemsToProcess)
            {
                var itemId = itemGroup.Key;

                if (IsChamsAlreadyApplied(itemId, mode))
                    continue;

                CacheItemMaterials(itemId, itemGroup.First().InteractiveClass);
                ApplyChamsToItemGroup(itemGroup, materialId);

                _activeLootChams[itemId] = mode;
                XMLogging.WriteLine($"[Loot Chams] Applied {mode} to {itemGroup.Count()} instances of {itemId}");
            }
        }

        private static bool IsChamsAlreadyApplied(string itemId, ChamsMode mode)
        {
            return _activeLootChams.TryGetValue(itemId, out var currentMode) && currentMode == mode;
        }

        private static void ApplyChamsToItemGroup(IGrouping<string, LootItem> itemGroup, int materialId)
        {
            foreach (var item in itemGroup)
            {
                LootItem.ApplyItemChams(item.InteractiveClass, materialId);
            }
        }

        private static void CacheItemMaterials(string itemId, ulong interactiveClass)
        {
            try
            {
                if (_cachedMaterials.ContainsKey(itemId))
                {
                    XMLogging.WriteLine($"[Loot Chams] Materials already cached for {itemId}");
                    return;
                }

                var materials = ExtractMaterialsFromRenderer(interactiveClass);

                if (materials.Count > 0)
                {
                    _cachedMaterials[itemId] = new CachedLootMaterial
                    {
                        ItemId = itemId,
                        OriginalMaterials = materials,
                        CacheTime = DateTime.UtcNow
                    };

                    XMLogging.WriteLine($"[Loot Chams] Successfully cached {materials.Count} original materials for {itemId}");
                }
                else
                {
                    XMLogging.WriteLine($"[Loot Chams] No materials found to cache for {itemId}");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to cache materials for {itemId}: {ex.Message}");
            }
        }

        private static Dictionary<int, int> ExtractMaterialsFromRenderer(ulong interactiveClass)
        {
            var materials = new Dictionary<int, int>();

            try
            {
                var rendererList = Memory.ReadPtr(interactiveClass + 0x90);
                if (rendererList == 0) return materials;

                int rendererCount = Memory.ReadValue<int>(rendererList + 0x18);
                if (rendererCount <= 0 || rendererCount > 1000) return materials;

                var rendererBase = Memory.ReadPtr(rendererList + 0x10);
                if (rendererBase == 0) return materials;

                for (int i = 0; i < rendererCount; i++)
                {
                    var renderer = Memory.ReadPtr(rendererBase + 0x20 + (ulong)(i * 0x8));
                    if (renderer == 0) continue;

                    ExtractRendererMaterials(renderer, materials);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Error extracting materials: {ex.Message}");
            }

            return materials;
        }

        private static void ExtractRendererMaterials(ulong renderer, Dictionary<int, int> materials)
        {
            try
            {
                var materialDict = Memory.ReadPtr(renderer + 0x10);
                if (materialDict == 0) return;

                int matCount = Memory.ReadValue<int>(materialDict + 0x158);
                if (matCount <= 0 || matCount > 100) return;

                var matArray = Memory.ReadPtr(materialDict + 0x148);
                if (matArray == 0) return;

                for (int j = 0; j < matCount; j++)
                {
                    var originalMaterial = Memory.ReadValue<int>(matArray + (ulong)(j * 0x4));
                    materials[j] = originalMaterial;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Error extracting renderer materials: {ex.Message}");
            }
        }

        private static void RevertObsoleteChams(HashSet<string> currentImportantItems, HashSet<string> currentQuestItems)
        {
            var itemsToRevert = new List<string>();

            foreach (var activeItem in _activeLootChams.Keys)
            {
                var shouldKeep = currentImportantItems.Contains(activeItem) || currentQuestItems.Contains(activeItem);
                if (!shouldKeep)
                    itemsToRevert.Add(activeItem);
            }

            foreach (var itemId in itemsToRevert)
            {
                RevertItemChams(itemId);
            }

            if (itemsToRevert.Any())
                XMLogging.WriteLine($"[Loot Chams] Reverted chams for {itemsToRevert.Count} items");
        }

        private static void RevertItemChams(string itemId)
        {
            try
            {
                if (!_cachedMaterials.TryGetValue(itemId, out var cached))
                {
                    XMLogging.WriteLine($"[Loot Chams] No cached materials found for {itemId}");
                    return;
                }

                var lootManager = Memory.Loot;
                if (lootManager?.FilteredLoot == null)
                    return;

                var itemInstances = lootManager.FilteredLoot
                    .Where(item => item.ID == itemId && item.InteractiveClass != 0)
                    .ToList();

                foreach (var item in itemInstances)
                {
                    RestoreItemMaterials(item.InteractiveClass, cached.OriginalMaterials);
                }

                _activeLootChams.TryRemove(itemId, out _);
                XMLogging.WriteLine($"[Loot Chams] Reverted chams for {itemInstances.Count} instances of {itemId}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to revert {itemId}: {ex.Message}");
            }
        }

        private static void RestoreItemMaterials(ulong interactiveClass, Dictionary<int, int> originalMaterials)
        {
            try
            {
                var rendererList = Memory.ReadPtr(interactiveClass + 0xC0);
                if (rendererList == 0) return;

                int rendererCount = Memory.ReadValue<int>(rendererList + 0x18);
                if (rendererCount <= 0 || rendererCount > 1000) return;

                var rendererBase = Memory.ReadPtr(rendererList + 0x10);
                if (rendererBase == 0) return;

                for (int i = 0; i < rendererCount; i++)
                {
                    var renderer = Memory.ReadPtr(rendererBase + 0x20 + (ulong)(i * 0x8));
                    if (renderer == 0) continue;

                    RestoreRendererMaterials(renderer, originalMaterials);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to restore materials: {ex.Message}");
            }
        }

        private static void RestoreRendererMaterials(ulong renderer, Dictionary<int, int> originalMaterials)
        {
            try
            {
                var materialDict = Memory.ReadPtr(renderer + 0x10);
                if (materialDict == 0) return;

                int matCount = Memory.ReadValue<int>(materialDict + 0x158);
                if (matCount <= 0 || matCount > 100) return;

                var matArray = Memory.ReadPtr(materialDict + 0x148);
                if (matArray == 0) return;

                for (int j = 0; j < matCount; j++)
                {
                    if (originalMaterials.TryGetValue(j, out var originalMaterial))
                        Memory.WriteValue(matArray + (ulong)(j * 0x4), originalMaterial);
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to restore renderer materials: {ex.Message}");
            }
        }

        #endregion

        #region Color Management

        private static (ChamsEntityType?, ChamsConfig.EntityChamsSettings) DetermineEntityTypeAndSettings(ChamsMode mode)
        {
            var importantSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.ImportantItem);
            var questSettings = ChamsConfig.GetEntitySettings(ChamsEntityType.QuestItem);

            if (importantSettings.Enabled && importantSettings.Mode == mode)
                return (ChamsEntityType.ImportantItem, importantSettings);

            if (questSettings.Enabled && questSettings.Mode == mode)
                return (ChamsEntityType.QuestItem, questSettings);

            return (null, null);
        }

        private static bool ApplyColorsToMaterial(RemoteBytes chamsColorMem, ChamsMaterial material, UnityColor visibleColor, UnityColor invisibleColor)
        {
            try
            {
                //Memory.WriteValue(nightVisionComponent + 0xE0, this.lastColor);
                //NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorVisible, visibleColor);
                //NativeMethods.SetMaterialColor(chamsColorMem, material.Address, material.ColorInvisible, invisibleColor);
                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to apply colors to material: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Cache Management

        private static void LoadCache()
        {
            try
            {
                var cache = Config.LowLevelCache.LootChamsCache;
                _cachedMaterials.Clear();

                foreach (var kvp in cache)
                {
                    _cachedMaterials[kvp.Key] = kvp.Value;
                }

                XMLogging.WriteLine($"[Loot Chams] Loaded {cache.Count} cached loot materials");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to load cache: {ex.Message}");
            }
        }

        private static void SaveCache()
        {
            try
            {
                var cache = Config.LowLevelCache.LootChamsCache;
                cache.Clear();

                foreach (var kvp in _cachedMaterials)
                {
                    cache[kvp.Key] = kvp.Value;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Config.LowLevelCache.SaveAsync();
                    }
                    catch (Exception ex)
                    {
                        XMLogging.WriteLine($"[Loot Chams] Failed to save cache: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Loot Chams] Failed to prepare cache for saving: {ex.Message}");
            }
        }

        #endregion
    }
}