using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class DisableFrostbite : MemWriteFeature<DisableFrostbite>
    {
        private bool _lastEnabledState;
        private ulong _cachedFrostbiteEffect;

        private const float FROSTBITE_DISABLED = 0.0f;
        private const float FROSTBITE_ENABLED  = 1.0f;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableFrostbite;
            set => MemWrites.Config.DisableFrostbite = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.Game is not LocalGameWorld game)
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var frostbite = GetFrostbiteEffect(game);
                if (!frostbite.IsValidVirtualAddress())
                    return;

                float opacity = Enabled ? FROSTBITE_DISABLED : FROSTBITE_ENABLED;
                writes.AddValueEntry(frostbite + Offsets.FrostbiteEffect._opacity, opacity);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    XMLogging.WriteLine(
                        $"[DisableFrostbite] {(Enabled ? "Disabled" : "Enabled")} (opacity={opacity})");
                };
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableFrostbite] ERROR: {ex}");
                _cachedFrostbiteEffect = 0;
            }
        }

        private ulong GetFrostbiteEffect(LocalGameWorld game)
        {
            if (_cachedFrostbiteEffect.IsValidVirtualAddress())
                return _cachedFrostbiteEffect;

            var fpsCam = game.CameraManager?.FPSCamera ?? 0;
            if (!fpsCam.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[FrostbiteEffect] Couldnt find fpsCam");
                return 0;
            }

            // EffectsController is a MonoBehaviour on the camera
            var effectsController = GameObjectManager
                .GetComponentFromBehaviour(fpsCam, "EffectsController");

            if (!effectsController.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[FrostbiteEffect] Couldnt find EffectsController in fps camera");
                return 0;
            }

            // FrostbiteEffect is another behaviour owned by EffectsController
            var frostbite = Memory.ReadPtr(effectsController + Offsets.EffectsController._frostbiteEffect);

            if (!frostbite.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[FrostbiteEffect] Wrong frostbite read.");
                return 0;
            }

            _cachedFrostbiteEffect = frostbite;
            return frostbite;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedFrostbiteEffect = default;
        }
    }
}
