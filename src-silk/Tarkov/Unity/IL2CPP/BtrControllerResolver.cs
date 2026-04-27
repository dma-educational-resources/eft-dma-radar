using eft_dma_radar.Silk.Tarkov;

namespace eft_dma_radar.Silk.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Resolves the <c>BtrController</c> singleton instance via the IL2CPP TypeInfoTable,
    /// using <c>BtrController.&lt;Instance&gt;k__BackingField</c> stored in the class's static fields.
    ///
    /// <para>
    /// Cached after first successful resolution. The backing instance pointer is stable for the
    /// lifetime of the raid, so callers should hold the value and only call <see cref="InvalidateCache"/>
    /// when raid state changes (e.g. raid end).
    /// </para>
    /// <para>
    /// This provides a <see cref="eft_dma_radar.Silk.Tarkov.GameWorld.LocalGameWorld"/>-independent
    /// path to the BTR controller, matching the pattern used by
    /// <see cref="EftHardSettingsResolver"/>.
    /// </para>
    /// </summary>
    internal static class BtrControllerResolver
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

                var typeIndex = Offsets.Special.BtrController_TypeIndex;
                if (typeIndex == 0)
                    return 0; // TypeIndex not yet resolved by the dumper.

                var typeInfoTablePtr = Memory.ReadPtr(
                    gaBase + Offsets.Special.TypeInfoTableRva, useCache: false);

                if (!typeInfoTablePtr.IsValidVirtualAddress())
                    return 0;

                var slot = typeInfoTablePtr + (ulong)typeIndex * (ulong)IntPtr.Size;

                var klassPtr = Memory.ReadPtr(slot, useCache: false);
                if (!klassPtr.IsValidVirtualAddress())
                    return 0;

                var staticFields = Memory.ReadPtr(
                    klassPtr + Offsets.Il2CppClass.StaticFields, useCache: false);

                if (!staticFields.IsValidVirtualAddress())
                    return 0;

                var instance = Memory.ReadPtr(
                    staticFields + Offsets.BtrController._instance, useCache: false);

                if (!instance.IsValidVirtualAddress())
                    return 0;

                _cachedInstance = instance;
                return instance;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BtrControllerResolver] Failed: {ex.Message}");
                _cachedInstance = 0;
                return 0;
            }
        }

        public static void InvalidateCache() => _cachedInstance = 0;
    }
}
