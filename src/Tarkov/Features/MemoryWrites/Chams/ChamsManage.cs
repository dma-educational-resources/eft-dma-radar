using eft_dma_radar.Common.Misc;
using System.Collections.Concurrent;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Chams
{
    public static class ChamsManager
    {
        public static event Action MaterialsUpdated;

        private static readonly ConcurrentDictionary<(ChamsMode, ChamsEntityType), ChamsMaterial> _materials = new();
        public static IReadOnlyDictionary<(ChamsMode, ChamsEntityType), ChamsMaterial> Materials => _materials;

        public static int ExpectedMaterialCount => 0;

        #region Public API

        public static bool Initialize() => false;

        public static bool ForceInitialize() => false;

        public static int GetMaterialIDForPlayer(ChamsMode mode, ChamsEntityType playerType)
        {
            if (!IsPlayerEntityType(playerType))
            {
                Log.WriteLine($"[Chams] Warning: {playerType} is not a valid player entity type");
                return -1;
            }

            if (Materials.TryGetValue((mode, playerType), out var material) && material.InstanceID != 0)
                return material.InstanceID;

            return -1;
        }

        public static int GetMaterialIDForLoot(ChamsMode mode, ChamsEntityType lootType)
        {
            if (!IsLootEntityType(lootType))
            {
                Log.WriteLine($"[Chams] Warning: {lootType} is not a valid loot entity type");
                return -1;
            }

            return mode switch
            {
                ChamsMode.Basic => -1,
                ChamsMode.Visible => -1,
                _ => Materials.TryGetValue((mode, lootType), out var material) && material.InstanceID != 0
                    ? material.InstanceID
                    : -1
            };
        }

        public static bool IsPlayerEntityType(ChamsEntityType entityType)
        {
            return entityType switch
            {
                ChamsEntityType.PMC or
                ChamsEntityType.Teammate or
                ChamsEntityType.AI or
                ChamsEntityType.Boss or
                ChamsEntityType.Guard or
                ChamsEntityType.PlayerScav or
                ChamsEntityType.AimbotTarget => true,
                _ => false
            };
        }

        public static bool IsLootEntityType(ChamsEntityType entityType)
        {
            return entityType switch
            {
                ChamsEntityType.Container or
                ChamsEntityType.QuestItem or
                ChamsEntityType.ImportantItem => true,
                _ => false
            };
        }

        public static bool AreMaterialsReadyForEntityType(ChamsEntityType entityType)
        {
            return false;
        }

        public static string GetEntityTypeStatus(ChamsEntityType entityType)
        {
            return $"{entityType}: 0/0 materials loaded";
        }

        public static bool SmartRefresh() => false;

        public static ChamsMaterialStatus GetDetailedStatus()
        {
            return new ChamsMaterialStatus
            {
                ExpectedCount = 0,
                LoadedCount = 0,
                WorkingCount = 0,
                FailedCount = 0,
                MissingCombos = [],
                FailedCombos = []
            };
        }

        public static void Reset()
        {
            _materials.Clear();
        }

        #endregion

        #region Private Implementation

        private static void NotifyMaterialsUpdated()
        {
            try
            {
                MaterialsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CHAMS] Error notifying materials updated: {ex.Message}");
            }
        }

        #endregion
    }
}