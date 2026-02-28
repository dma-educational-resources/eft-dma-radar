using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    internal static class EftClientBackendSessionResolver
    {
        private static ulong _cachedList;

        public static ulong GetListPtr()
        {
            if (_cachedList.IsValidVirtualAddress())
                return _cachedList;

            try
            {
                var gaBase = Memory.GameAssemblyBase;
                if (gaBase == 0)
                    return 0;

                // 1. Read TypeInfo table
                var typeInfoTable = Memory.ReadPtr(
                    gaBase + Offsets.Special.TypeInfoTableRva, false);

                if (!typeInfoTable.IsValidVirtualAddress())
                    return 0;

                // 2. TypeIndex ¡ú klass*
                ulong slot = typeInfoTable + 
                    (ulong)Offsets.Special.GlobalConfiguration_TypeIndex * (ulong)IntPtr.Size;

                var klassPtr = Memory.ReadPtr(slot, false);
                if (!klassPtr.IsValidVirtualAddress())
                    return 0;

                // 3. Read static fields block
                var staticFields = Memory.ReadPtr(
                    klassPtr + Offsets.Il2CppClass.StaticFields, false);

                if (!staticFields.IsValidVirtualAddress())
                    return 0;

                // 4. Read list pointer ¡ª THIS is the important part
                //    InstanceOffset == 0x0 ¡ú static field #0
                var listPtr = Memory.ReadPtr(
                    staticFields + Offsets.EFT.GPUInstancerManager.Instance, false);

                if (!listPtr.IsValidVirtualAddress())
                    return 0;

                _cachedList = listPtr;

                return listPtr;
            }
            catch
            {
                _cachedList = 0;
                return 0;
            }
        }

        public static void InvalidateCache()
        {
            _cachedList = 0;
        }
    }

}
