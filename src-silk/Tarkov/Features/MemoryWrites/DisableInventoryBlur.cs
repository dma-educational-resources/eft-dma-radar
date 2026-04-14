using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class DisableInventoryBlur : MemWriteFeature<DisableInventoryBlur>
    {
        private bool _lastEnabledState;
        private ulong _cachedBlurEffect;

        private const int BLUR_COUNT_DISABLED = 0;
        private const int BLUR_COUNT_ENABLED = 5;
        private const int UPSAMPLE_DISABLED = 2048;
        private const int UPSAMPLE_ENABLED = 256;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.DisableInventoryBlur;
            set => SilkProgram.Config.MemWrites.DisableInventoryBlur = value;
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

                var blur = GetBlurEffect(game);
                if (!blur.IsValidVirtualAddress())
                    return;

                var (blurCount, upsample) = Enabled
                    ? (BLUR_COUNT_DISABLED, UPSAMPLE_DISABLED)
                    : (BLUR_COUNT_ENABLED, UPSAMPLE_ENABLED);

                writes.AddValueEntry(blur + Offsets.InventoryBlur._blurCount, blurCount);
                writes.AddValueEntry(blur + Offsets.InventoryBlur._upsampleTexDimension, upsample);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[DisableInventoryBlur] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[DisableInventoryBlur]: {ex.Message}");
                _cachedBlurEffect = default;
            }
        }

        private ulong GetBlurEffect(LocalGameWorld game)
        {
            if (_cachedBlurEffect.IsValidVirtualAddress())
                return _cachedBlurEffect;

            var fps = game.CameraManager?.FPSCamera ?? 0ul;
            if (!fps.IsValidVirtualAddress()) return 0;

            var comp = GOM.GetComponentFromBehaviour(fps, "InventoryBlur");
            if (comp.IsValidVirtualAddress())
                _cachedBlurEffect = comp;
            return comp;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedBlurEffect = default;
        }
    }
}
