using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class ThirdPerson : MemWriteFeature<ThirdPerson>
    {
        private bool _lastEnabledState;
        private ulong _cachedHandsContainer;

        private static readonly Vector3 THIRD_PERSON_ON = new(0.04f, 0.14f, -2.2f);
        private static readonly Vector3 THIRD_PERSON_OFF = new(0.04f, 0.04f, 0.05f);

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.ThirdPerson;
            set => SilkProgram.Config.MemWrites.ThirdPerson = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var handsContainer = GetHandsContainer(localPlayer);
                if (!handsContainer.IsValidVirtualAddress())
                    return;

                var offset = Enabled ? THIRD_PERSON_ON : THIRD_PERSON_OFF;
                writes.AddValueEntry(handsContainer + Offsets.HandsContainer.CameraOffset, offset);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    Log.WriteLine($"[ThirdPerson] {(Enabled ? "Enabled" : "Disabled")}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[ThirdPerson]: {ex.Message}");
                _cachedHandsContainer = default;
            }
        }

        private ulong GetHandsContainer(LocalPlayer localPlayer)
        {
            if (_cachedHandsContainer.IsValidVirtualAddress())
                return _cachedHandsContainer;

            if (!localPlayer.PWA.IsValidVirtualAddress()) return 0;

            var hc = Memory.ReadPtr(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.HandsContainer);
            if (hc.IsValidVirtualAddress())
                _cachedHandsContainer = hc;
            return hc;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _cachedHandsContainer = default;
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState = default;
            _cachedHandsContainer = default;
        }
    }
}
