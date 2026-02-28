using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
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
                    gaBase + Offsets.Special.TypeInfoTableRva,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(typeInfoTablePtr))
                    return 0;

                var index = (ulong)Offsets.Special.EFTHardSettings_TypeIndex;
                var slot  = typeInfoTablePtr + index * (ulong)IntPtr.Size;

                var klassPtr = Memory.ReadPtr(slot, useCache: false);
                if (!Utils.IsValidVirtualAddress(klassPtr))
                    return 0;

                var staticFieldsBase = Memory.ReadPtr(
                    klassPtr + Offsets.Il2CppClass.StaticFields,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(staticFieldsBase))
                    return 0;

                var instance = Memory.ReadPtr(
                    staticFieldsBase + Offsets.EFT.EFTHardSettings._instance,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(instance))
                    return 0;

                _cachedInstance = instance;

                try
                {
                    var namePtr = Memory.ReadPtr(klassPtr + Offsets.Il2CppClass.Name, false);
                    var nsPtr   = Memory.ReadPtr(klassPtr + Offsets.Il2CppClass.Namespace, false);
                    var name    = Memory.ReadString(namePtr, 64, false) ?? "??";
                    var ns      = Memory.ReadString(nsPtr, 64, false) ?? "";
                    Debug.WriteLine($"[EftHardSettingsResolver] Resolved '{ns}.{name}' instance @ 0x{instance:X}");
                }
                catch
                {
                    // best-effort logging only
                }

                return instance;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EftHardSettingsResolver] Failed to resolve instance: {ex}");
                _cachedInstance = 0;
                return 0;
            }
        }

        public static void InvalidateCache()
        {
            _cachedInstance = 0;
        }
    }
}
