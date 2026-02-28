using System;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;

namespace eft_dma_radar.Common.Unity.IL2CPP
{
    /// <summary>
    /// IL2CPP singleton resolver.
    /// Supports:
    ///  - Find by full class name (Namespace.Class)
    ///  - Find by TypeIndex (fast path)
    /// </summary>
    internal static class Il2CppSingletonResolver
    {
        // ------------------------------------------------------------
        // PUBLIC API
        // ------------------------------------------------------------

        /// <summary>
        /// Find a singleton instance by full class name (Namespace.Class).
        /// Slow (O(N)), intended for startup / cache building.
        /// </summary>
        public static ulong FindByName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return 0;

            ulong gaBase = Memory.GameAssemblyBase;
            if (!gaBase.IsValidVirtualAddress())
                return 0;

            ulong typeInfoTable = Memory.ReadPtr(
                gaBase + Offsets.Special.TypeInfoTableRva,
                useCache: false);

            if (!typeInfoTable.IsValidVirtualAddress())
                return 0;

            // Assembly-CSharp metadata (from your dumper)
            uint typeStart = Offsets.AssemblyCSharp.TypeStart;
            uint typeCount = Offsets.AssemblyCSharp.TypeCount;

            for (uint i = 0; i < typeCount; i++)
            {
                ulong klass = Memory.ReadPtr(
                    typeInfoTable + (ulong)(typeStart + i) * (ulong)IntPtr.Size,
                    useCache: false);

                if (!klass.IsValidVirtualAddress())
                    continue;

                string name = ReadIl2CppString(klass + Offsets.Il2CppClass.Name);
                string ns   = ReadIl2CppString(klass + Offsets.Il2CppClass.Namespace);

                string full = string.IsNullOrEmpty(ns)
                    ? name
                    : $"{ns}.{name}";

                if (!full.Equals(fullName, StringComparison.Ordinal))
                    continue;

                return ReadSingletonFromClass(klass);
            }

            return 0;
        }

        /// <summary>
        /// Find a singleton instance by TypeIndex (O(1), preferred).
        /// </summary>
        public static ulong FindByTypeIndex(uint typeIndex, uint staticFieldOffset = 0)
        {
            ulong gaBase = Memory.GameAssemblyBase;
            if (!gaBase.IsValidVirtualAddress())
                return 0;

            ulong typeInfoTable = Memory.ReadPtr(
                gaBase + Offsets.Special.TypeInfoTableRva,
                useCache: false);

            if (!typeInfoTable.IsValidVirtualAddress())
                return 0;

            ulong klass = Memory.ReadPtr(
                typeInfoTable + (ulong)typeIndex * (ulong)IntPtr.Size,
                useCache: false);

            if (!klass.IsValidVirtualAddress())
                return 0;

            ulong staticFields = Memory.ReadPtr(
                klass + Offsets.Il2CppClass.StaticFields,
                useCache: false);

            if (!staticFields.IsValidVirtualAddress())
                return 0;

            ulong instance = Memory.ReadPtr(
                staticFields + staticFieldOffset,
                useCache: false);

            return instance.IsValidVirtualAddress() ? instance : 0;
        }

        // ------------------------------------------------------------
        // INTERNAL HELPERS
        // ------------------------------------------------------------

        private static ulong ReadSingletonFromClass(ulong klass)
        {
            ulong staticFields = Memory.ReadPtr(
                klass + Offsets.Il2CppClass.StaticFields,
                useCache: false);

            if (!staticFields.IsValidVirtualAddress())
                return 0;

            // EFT pattern:
            // singleton instance is usually static field #0
            ulong instance = Memory.ReadPtr(staticFields, useCache: false);

            return instance.IsValidVirtualAddress() ? instance : 0;
        }

        private static string ReadIl2CppString(ulong address)
        {
            ulong strPtr = Memory.ReadPtr(address, useCache: false);
            if (!strPtr.IsValidVirtualAddress())
                return string.Empty;

            // Il2CppString layout:
            // +0x10 = length
            // +0x14 = UTF-16 chars
            int len = Memory.ReadValue<int>(strPtr + 0x10, useCache: false);
            if (len <= 0 || len > 256)
                return string.Empty;

            return Memory.ReadString(strPtr + 0x14, len);
        }
    }
}
