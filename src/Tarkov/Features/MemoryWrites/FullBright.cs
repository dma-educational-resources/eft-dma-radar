using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Tarkov.GameWorld;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class FullBright : MemWriteFeature<FullBright>
    {
        private bool  _lastEnabledState;
        private float _lastBrightness;

        // Make the pointer global for the process; it¡¯s global game state anyway
        private static ulong _cachedLevelSettings;
        private static volatile bool _resolving;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day",
            "factory4_night",
            "laboratory",
            "Labyrinth"
        };

        public override bool Enabled
        {
            get => MemWrites.Config.FullBright.Enabled;
            set => MemWrites.Config.FullBright.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(200);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {          
                // No writes at all if memwrites disabled globally
                if (!MemWrites.Enabled)
                    return;
                if (Memory.Game is not LocalGameWorld game)
                    return;      
                if (ExcludedMaps.Contains(game.MapID))
                    return;

                var config           = MemWrites.Config.FullBright;

                if(!config.Enabled)
                    return;
                    
                var configBrightness = config.Intensity;

                var stateChanged      = Enabled != _lastEnabledState;
                var brightnessChanged = Math.Abs(configBrightness - _lastBrightness) > 0.001f;

                // Nothing to do this tick
                if (!stateChanged && !brightnessChanged)
                    return;

                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                // Resolve LevelSettings SAFELY (non-blocking)
                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                ulong levelSettings = _cachedLevelSettings;

                // 1) Use global Memory.LevelSettings if cache is empty
                if (!levelSettings.IsValidVirtualAddress())
                {
                    levelSettings = Memory.LevelSettings;
                    if (levelSettings.IsValidVirtualAddress())
                    {
                        _cachedLevelSettings = levelSettings;
                    }
                }

                // 2) If still invalid, *schedule* a resolver but don¡¯t block
                if (!levelSettings.IsValidVirtualAddress())
                {
                    KickOffLevelSettingsResolve();
                    // Skip this tick; other features keep running
                    // XMLogging.WriteLine("[FullBright] LevelSettings not resolved yet, skipping tick.");
                    return;
                }

                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                // Queue writes
                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                ApplyFullBrightSettings(writes, levelSettings, Enabled, configBrightness);

                // Commit state only after a successful scatter write
                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    _lastBrightness   = configBrightness;

                    if (Enabled)
                        XMLogging.WriteLine($"[FullBright] Enabled (Intensity: {configBrightness:F2})");
                    else
                        XMLogging.WriteLine("[FullBright] Disabled");
                };
            }
            catch (Exception ex)
            {
                // HARD NON-FATAL GUARANTEE:
                // Any bug in this feature affects ONLY FullBright, never the whole memwrite batch.
                XMLogging.WriteLine($"[FullBright] ERROR (non-fatal): {ex}");
                _cachedLevelSettings = 0; // force re-resolve next time
                // DO NOT rethrow
            }
        }

        /// <summary>
        /// Fire-and-forget resolver; NEVER blocks the feature thread.
        /// </summary>
        private static void KickOffLevelSettingsResolve()
        {
            if (_resolving)
                return;

            _resolving = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var levelSettings = LevelSettingsResolver.GetLevelSettings();
                    if (levelSettings.IsValidVirtualAddress())
                    {
                        _cachedLevelSettings = levelSettings;
                        XMLogging.WriteLine($"[FullBright] Resolved LevelSettings @ 0x{levelSettings:X}");
                    }
                    else
                    {
                        XMLogging.WriteLine("[FullBright] LevelSettingsResolver returned invalid pointer.");
                    }
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[FullBright] LevelSettingsResolver error: {ex.Message}");
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

                var equatorColor = new UnityColor(brightness, brightness, brightness);
                var groundColor  = new UnityColor(0f, 0f, 0f);

                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.EquatorColor, ref equatorColor);
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.GroundColor, ref groundColor);
            }
            else
            {
                // Minimal revert ¨C you can extend this if you later cache original values.
                writes.AddValueEntry(levelSettings + Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Flat);
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState    = default;
            _lastBrightness      = default;
            _cachedLevelSettings = default;
            _resolving           = false;

            // Let resolver forget stale pointers at raid start only,
            // not every time memwrites tick.
            LevelSettingsResolver.Reset();
        }

        private enum AmbientMode : int
        {
            Skybox,
            Trilight,
            Flat = 3,
            Custom
        }
    }
}
