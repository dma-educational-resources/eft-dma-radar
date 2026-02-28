using System;
using System.Diagnostics;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Data;
using eft_dma_radar.Common.Unity;
using SDK;

namespace eft_dma_radar.Tarkov.Unity.IL2CPP
{
    internal static class EftWeatherControllerResolver
    {
        private static ulong _cachedWeatherDebug;
        private static readonly object _lock = new();

        /// <summary>
        /// Return EFT.WeatherController.WeatherDebug singleton (cached).
        /// Returns 0 if not available yet.
        /// </summary>
        public static ulong GetWeatherDebug()
        {
            // Fast path: already cached
            lock (_lock)
            {
                if (Utils.IsValidVirtualAddress(_cachedWeatherDebug))
                    return _cachedWeatherDebug;
            }

            try
            {
                var gaBase = Memory.GameAssemblyBase;
                if (gaBase == 0)
                    return 0;

                // 1) TypeInfo table pointer
                var typeInfoTablePtr = SafeReadPtr(
                    gaBase + Offsets.Special.TypeInfoTableRva,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(typeInfoTablePtr))
                    return 0;

                // 2) TypeIndex ¡ú klass*
                var index = (ulong)Offsets.Special.WeatherController_TypeIndex;
                var slot  = typeInfoTablePtr + index * (ulong)IntPtr.Size;

                var klassPtr = SafeReadPtr(slot, useCache: false);
                if (!Utils.IsValidVirtualAddress(klassPtr))
                    return 0;

                // 3) Static fields block
                var staticFieldsBase = SafeReadPtr(
                    klassPtr + Offsets.Il2CppClass.StaticFields,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(staticFieldsBase))
                    return 0;

                // 4) WeatherController singleton instance
                var controllerInstance = SafeReadPtr(
                    staticFieldsBase + Offsets.EFT.WeatherController.Instance,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(controllerInstance))
                    return 0;

                // 5) Nested WeatherDebug object
                var weatherDebug = SafeReadPtr(
                    controllerInstance + Offsets.WeatherController.WeatherDebug,
                    useCache: false);

                if (!Utils.IsValidVirtualAddress(weatherDebug))
                    return 0;

                lock (_lock)
                {
                    _cachedWeatherDebug = weatherDebug;
                }

                // Optional success log (like EFTHardSettings)
                try
                {
                    var namePtr = SafeReadPtr(klassPtr + Offsets.Il2CppClass.Name, useCache: false);
                    var nsPtr   = SafeReadPtr(klassPtr + Offsets.Il2CppClass.Namespace, useCache: false);

                    var name = Utils.IsValidVirtualAddress(namePtr)
                        ? Memory.ReadString(namePtr, 64, useCache: false)
                        : "??";

                    var ns = Utils.IsValidVirtualAddress(nsPtr)
                        ? Memory.ReadString(nsPtr, 64, useCache: false)
                        : "";

                    Debug.WriteLine($"[EftWeatherControllerResolver] Resolved '{ns}.{name}.WeatherDebug' @ 0x{weatherDebug:X}");
                }
                catch
                {
                    // best-effort logging only
                }

                return weatherDebug;
            }
            catch (Exception ex)
            {
                // Single compact error per failure; no inner spam
                Debug.WriteLine($"[EftWeatherControllerResolver] Failed to resolve WeatherDebug: {ex.Message}");
                lock (_lock)
                {
                    _cachedWeatherDebug = 0;
                }
                return 0;
            }
        }

        /// <summary>
        /// Safe wrapper that NEVER calls ReadPtr on addr == 0 or other invalid VAs.
        /// Returns 0 instead of throwing.
        /// </summary>
        private static ulong SafeReadPtr(ulong addr, bool useCache)
        {
            if (!Utils.IsValidVirtualAddress(addr))
                return 0;

            return Memory.ReadPtr(addr, useCache);
        }

        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedWeatherDebug = 0;
            }
        }
    }
}
