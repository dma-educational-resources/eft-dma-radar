using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    /// <summary>
    /// Shared helper for resolving the TarkovApplication objectClass pointer from the GOM.
    /// Primary: klass-pointer-based scan using TarkovApplication_TypeIndex (fast — avoids
    /// reading name strings, saves ~2 DMA reads per component check).
    /// Fallback: class-name-based scan.
    /// The result is cached for the lifetime of the game process.
    /// </summary>
    internal static class TarkovApplicationHelper
    {
        private static ulong _cachedObjectClass;
        private static ulong _cachedKlassPtr;

        /// <summary>
        /// Resolves the TarkovApplication objectClass pointer from the GOM.
        /// Returns a valid pointer, or 0 on failure.
        /// </summary>
        public static ulong GetObjectClass()
        {
            if (_cachedObjectClass.IsValidVirtualAddress())
                return _cachedObjectClass;

            try
            {
                var gomAddr = Memory.GOM;
                if (!gomAddr.IsValidVirtualAddress())
                    return 0;

                var gom = GameObjectManager.Get(gomAddr);
                ulong result = 0;

                // Primary: klass-pointer-based GOM scan
                try
                {
                    var klassPtr = _cachedKlassPtr;
                    if (!klassPtr.IsValidVirtualAddress())
                    {
                        klassPtr = Il2CppDumper.ResolveKlassByTypeIndex(
                            Offsets.Special.TarkovApplication_TypeIndex);
                        if (klassPtr.IsValidVirtualAddress())
                            _cachedKlassPtr = klassPtr;
                    }

                    if (klassPtr.IsValidVirtualAddress())
                        result = gom.FindBehaviourByKlassPtr(klassPtr);
                }
                catch { }

                // Fallback: class name scan
                if (!result.IsValidVirtualAddress())
                {
                    try
                    {
                        result = gom.FindBehaviourByClassName("TarkovApplication");
                    }
                    catch { return 0; }
                }

                if (result.IsValidVirtualAddress())
                    _cachedObjectClass = result;

                return result;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Invalidate cached pointer (call on process re-attach).
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedObjectClass = 0;
            // Don't clear _cachedKlassPtr — it stays valid for the game process lifetime
        }
    }
}
