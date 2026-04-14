using eft_dma_radar.Silk.Tarkov;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Resolves the EFTHardSettings singleton instance via the IL2CPP TypeInfoTable.
    /// Cached after first successful resolution.
    /// </summary>
    internal static class EftHardSettingsResolver
    {
        private static ulong _cachedInstance;

        public static ulong GetInstance()
        {
            if (_cachedInstance.IsValidVirtualAddress())
                return _cachedInstance;

            try
            {
                var gaBase = Memory.GameAssemblyBase;
                if (gaBase == 0)
                    return 0;

                var typeInfoTablePtr = Memory.ReadPtr(
                    gaBase + Offsets.Special.TypeInfoTableRva, useCache: false);

                if (!typeInfoTablePtr.IsValidVirtualAddress())
                    return 0;

                var index = (ulong)Offsets.Special.EFTHardSettings_TypeIndex;
                var slot = typeInfoTablePtr + index * (ulong)IntPtr.Size;

                var klassPtr = Memory.ReadPtr(slot, useCache: false);
                if (!klassPtr.IsValidVirtualAddress())
                    return 0;

                var staticFields = Memory.ReadPtr(
                    klassPtr + Offsets.Il2CppClass.StaticFields, useCache: false);

                if (!staticFields.IsValidVirtualAddress())
                    return 0;

                var instance = Memory.ReadPtr(
                    staticFields + Offsets.EFTHardSettings._instance, useCache: false);

                if (!instance.IsValidVirtualAddress())
                    return 0;

                _cachedInstance = instance;
                return instance;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftHardSettingsResolver] Failed: {ex.Message}");
                _cachedInstance = 0;
                return 0;
            }
        }

        public static void InvalidateCache() => _cachedInstance = 0;
    }
}
