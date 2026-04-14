using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class FullBright : MemWriteFeature<FullBright>
    {
        private bool  _lastEnabledState;
        private float _lastBrightness;
        private ulong _cachedLevelSettings;
        private bool  _resolving;

        // Ambient mode values from UnityEngine.AmbientMode
        private enum AmbientMode : int
        {
            Skybox  = 0,
            Trilight = 1,
            Flat    = 3,
            Custom  = 4,
        }

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.FullBright.Enabled;
            set => SilkProgram.Config.MemWrites.FullBright.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var configBrightness = SilkProgram.Config.MemWrites.FullBright.Brightness;
                var stateChanged     = Enabled != _lastEnabledState;
                var brightnessChanged = Math.Abs(_lastBrightness - configBrightness) > 0.01f;

                if (!stateChanged && !brightnessChanged)
                    return;

                var levelSettings = GetLevelSettings();
                if (!levelSettings.IsValidVirtualAddress())
                    return;

                ApplyFullBrightSettings(writes, levelSettings, Enabled, configBrightness);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    _lastBrightness   = configBrightness;
                    Log.WriteLine(Enabled
                        ? $"[FullBright] Enabled (brightness={configBrightness:F2})"
                        : "[FullBright] Disabled");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[FullBright]: {ex.Message}");
                _cachedLevelSettings = default;
            }
        }

        private ulong GetLevelSettings()
        {
            if (_cachedLevelSettings.IsValidVirtualAddress())
                return _cachedLevelSettings;

            KickOffLevelSettingsResolve();
            return 0;
        }

        private void KickOffLevelSettingsResolve()
        {
            if (_resolving)
                return;

            _resolving = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var ls = LevelSettingsResolver.GetLevelSettings();
                    if (ls.IsValidVirtualAddress())
                    {
                        _cachedLevelSettings = ls;
                        Log.WriteLine($"[FullBright] Resolved LevelSettings @ 0x{ls:X}");
                    }
                    else
                    {
                        Log.WriteLine("[FullBright] LevelSettingsResolver returned invalid pointer.");
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[FullBright] LevelSettingsResolver error: {ex.Message}");
                    _cachedLevelSettings = 0;
                }
                finally
                {
                    _resolving = false;
                }
            });
        }

        private static void ApplyFullBrightSettings(
            ScatterWriteHandle writes,
            ulong levelSettings,
            bool enabled,
            float brightness)
        {
            if (!levelSettings.IsValidVirtualAddress())
                return;

            if (enabled)
            {
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Trilight);
                // Equator color: full white at requested brightness
                var equatorColor = new Vector4(brightness, brightness, brightness, 1f);
                var groundColor  = new Vector4(0f, 0f, 0f, 1f);
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.EquatorColor, equatorColor);
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.GroundColor,  groundColor);
            }
            else
            {
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Flat);
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState    = default;
            _lastBrightness      = default;
            _cachedLevelSettings = default;
            _resolving           = false;
            LevelSettingsResolver.Reset();
        }
    }
}
