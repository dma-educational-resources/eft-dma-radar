using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class ClearWeather : MemWriteFeature<ClearWeather>
    {
        private bool _lastEnabledState;
        private ulong _cachedWeatherDebug;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory",
            "Labyrinth"
        };

        public override bool Enabled
        {
            get => MemWrites.Config.ClearWeather;
            set => MemWrites.Config.ClearWeather = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (ExcludedMaps.Contains(game.MapID))
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var weatherDebug = GetWeatherDebug();
                    if (!weatherDebug.IsValidVirtualAddress())
                        return;

                    if (Enabled)
                        ApplyClearWeatherSettings(writes, weatherDebug);
                    else
                        writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.isEnabled, false);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[ClearWeather] {(Enabled ? "Enabled" : "Disabled")}");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ClearWeather]: {ex}");
                _cachedWeatherDebug = default;
            }
        }

        private ulong GetWeatherDebug()
        {
            // Use cache first
            if (_cachedWeatherDebug.IsValidVirtualAddress())
                return _cachedWeatherDebug;

            // 1) Primary resolver (TypeInfoTable + static fields)
            var dbg = EftWeatherControllerResolver.GetWeatherDebug();
            if (dbg.IsValidVirtualAddress())
            {
                _cachedWeatherDebug = dbg;
                return dbg;
            }

            // 2) Fallback IL2CPP search (class name lookup)
            try
            {
                var klass = Il2CppClass.Find(
                    "Assembly-CSharp",
                    "EFT.Weather.WeatherController",
                    out var klassPtr);

                var k = klassPtr.IsValidVirtualAddress() ? klassPtr : klass;
                if (!k.IsValidVirtualAddress())
                    return 0x0;

                // Static fields block (same pattern as HardSettings)
                ulong staticFieldsBase = Il2CppClass.GetStaticFieldData(k);
                if (!staticFieldsBase.IsValidVirtualAddress())
                    return 0x0;

                // WeatherController.Instance
                var controller = Memory.ReadPtr(
                    staticFieldsBase + Offsets.EFT.WeatherController.Instance);

                if (!controller.IsValidVirtualAddress())
                    return 0x0;

                // Controller.WeatherDebug
                var weatherDebug = Memory.ReadPtr(
                    controller + Offsets.WeatherController.WeatherDebug);

                if (!weatherDebug.IsValidVirtualAddress())
                    return 0x0;

                // Optional sanity check ¨C do NOT throw, just bail if weird
                try
                {
                    var name = ObjectClass.ReadName(weatherDebug);
                    if (!string.Equals(name, "WeatherDebug", StringComparison.Ordinal))
                    {
                        XMLogging.WriteLine($"[ClearWeather] Fallback found unexpected object '{name}', skipping.");
                        return 0x0;
                    }
                }
                catch
                {
                    // If name read fails, just skip using this pointer
                    return 0x0;
                }

                _cachedWeatherDebug = weatherDebug;
                return weatherDebug;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[ClearWeather] Fallback WeatherDebug search failed: {ex.Message}");
                return 0x0;
            }
        }

        private static void ApplyClearWeatherSettings(ScatterWriteHandle writes, ulong weatherDebug)
        {
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.isEnabled, true);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.WindMagnitude, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.CloudDensity, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.Fog, 0.001f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.Rain, 0f);
            writes.AddValueEntry(weatherDebug + Offsets.WeatherDebug.LightningThunderProbability, 0f);
        }

        public override void OnRaidStart()
        {
            _lastEnabledState   = default;
            _cachedWeatherDebug = default;
            EftWeatherControllerResolver.InvalidateCache();
        }
    }
}
