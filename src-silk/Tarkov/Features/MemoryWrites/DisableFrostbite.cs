using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class DisableFrostbite : MemWriteFeature<DisableFrostbite>
    {
        private bool _lastEnabledState;
        private ulong _cachedFrostbiteEffect;

        private const float FROSTBITE_DISABLED = 0f;
        private const float FROSTBITE_ENABLED = 1f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.DisableFrostbite;
            set => SilkProgram.Config.MemWrites.DisableFrostbite = value;
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
                    Log.WriteLine($"[DisableFrostbite] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DisableFrostbite]: {ex.Message}");
                _cachedFrostbiteEffect = default;
            }
        }

        private ulong GetFrostbiteEffect(LocalGameWorld game)
        {
            if (_cachedFrostbiteEffect.IsValidVirtualAddress())
                return _cachedFrostbiteEffect;

            var fps = game.CameraManager?.FPSCamera ?? 0ul;
            if (!fps.IsValidVirtualAddress()) return 0;

            var effectsController = GOM.GetComponentFromBehaviour(fps, "EffectsController");
            if (!effectsController.IsValidVirtualAddress()) return 0;

            var frostbite = Memory.ReadPtr(effectsController + Offsets.EffectsController._frostbiteEffect);
            if (frostbite.IsValidVirtualAddress())
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
