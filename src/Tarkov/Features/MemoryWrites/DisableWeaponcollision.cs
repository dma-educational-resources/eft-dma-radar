using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class DisableWeaponCollision : MemWriteFeature<DisableWeaponCollision>
    {
        private bool _lastEnabledState;
        private ulong _cachedEFTHardSettingsInstance;

        private const uint ORIGINAL_WEAPON_OCCLUSION_LAYERS = 1082136832;
        private const uint DISABLED_WEAPON_OCCLUSION_LAYERS = 0;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableWeaponCollision;
            set => MemWrites.Config.DisableWeaponCollision = value;
        }

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                var hardSettingsInstance = GetEFTHardSettingsInstance();
                if (!hardSettingsInstance.IsValidVirtualAddress())
                    return;

                if (Enabled != _lastEnabledState)
                {
                    var targetLayers = Enabled ? DISABLED_WEAPON_OCCLUSION_LAYERS : ORIGINAL_WEAPON_OCCLUSION_LAYERS;
                    writes.AddValueEntry(hardSettingsInstance + Offsets.EFT.EFTHardSettings.WEAPON_OCCLUSION_LAYERS, targetLayers);

                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = Enabled;
                        XMLogging.WriteLine($"[DisableWeaponCollision] {(Enabled ? "Enabled" : "Disabled")} (Layers: {targetLayers})");
                    };
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableWeaponCollision]: {ex}");
                _cachedEFTHardSettingsInstance = default;
            }
        }

        private ulong GetEFTHardSettingsInstance()
        {
            if (_cachedEFTHardSettingsInstance.IsValidVirtualAddress())
                return _cachedEFTHardSettingsInstance;

            try
            {
                var hardSettingsInstance = EftHardSettingsResolver.GetInstance();
                if (!Utils.IsValidVirtualAddress(hardSettingsInstance))
                {
                    Debug.WriteLine("[FastDuck] EFTHardSettings.Instance not found.");
                    EftHardSettingsResolver.InvalidateCache();
                    _lastEnabledState = Enabled;
                    return 0x0;
                }

                _cachedEFTHardSettingsInstance = hardSettingsInstance;
                return hardSettingsInstance;
            }
            catch
            {
                return 0x0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState           = default;
            _cachedEFTHardSettingsInstance = default;
        }
    }
}
