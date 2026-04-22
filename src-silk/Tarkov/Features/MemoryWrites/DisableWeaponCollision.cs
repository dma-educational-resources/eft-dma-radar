using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class DisableWeaponCollision : MemWriteFeature<DisableWeaponCollision>
    {
        private bool _lastEnabledState;
        private ulong _cachedHardSettings;

        private const uint ORIGINAL_WEAPON_OCCLUSION_LAYERS = 1082136832;
        private const uint DISABLED_WEAPON_OCCLUSION_LAYERS = 0;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.DisableWeaponCollision;
            set => SilkProgram.Config.MemWrites.DisableWeaponCollision = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var hardSettings = GetHardSettings();
                if (!hardSettings.IsValidVirtualAddress())
                    return;

                var targetLayers = Enabled ? DISABLED_WEAPON_OCCLUSION_LAYERS : ORIGINAL_WEAPON_OCCLUSION_LAYERS;
                writes.AddValueEntry(hardSettings + Offsets.EFTHardSettings.WEAPON_OCCLUSION_LAYERS, targetLayers);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[DisableWeaponCollision] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DisableWeaponCollision]: {ex.Message}");
                _cachedHardSettings = default;
            }
        }

        private ulong GetHardSettings()
        {
            if (_cachedHardSettings.IsValidVirtualAddress())
                return _cachedHardSettings;

            var hs = EftHardSettingsResolver.GetInstance();
            if (hs.IsValidVirtualAddress())
                _cachedHardSettings = hs;
            return hs;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedHardSettings = default;
            EftHardSettingsResolver.InvalidateCache();
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState = default;
            _cachedHardSettings = default;
        }
    }
}
