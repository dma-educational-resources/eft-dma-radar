using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class InfStamina : MemWriteFeature<InfStamina>
    {
        private bool  _lastEnabledState;
        private ulong _cachedPhysical;
        private ulong _cachedStaminaObj;
        private ulong _cachedOxygenObj;

        private const float MAX_STAMINA       = 100f;
        private const float MAX_OXYGEN        = 350f;
        private const float REFILL_THRESHOLD  = 0.33f;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.InfStamina;
            set => SilkProgram.Config.MemWrites.InfStamina = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var stateChanged = Enabled != _lastEnabledState;
                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                        Log.WriteLine("[InfStamina] Disabled");
                    }
                    return;
                }

                var (staminaObj, oxygenObj) = GetStaminaObjects(localPlayer);
                if (!staminaObj.IsValidVirtualAddress() || !oxygenObj.IsValidVirtualAddress())
                    return;

                float currentStamina = Memory.ReadValue<float>(staminaObj + Offsets.PhysicalValue.Current, false);
                float currentOxygen  = Memory.ReadValue<float>(oxygenObj  + Offsets.PhysicalValue.Current, false);

                if (currentStamina < MAX_STAMINA * REFILL_THRESHOLD)
                    writes.AddValueEntry(staminaObj + Offsets.PhysicalValue.Current, MAX_STAMINA);

                if (currentOxygen < MAX_OXYGEN * REFILL_THRESHOLD)
                    writes.AddValueEntry(oxygenObj + Offsets.PhysicalValue.Current, MAX_OXYGEN);

                if (stateChanged)
                {
                    writes.Callbacks += () =>
                    {
                        _lastEnabledState = true;
                        Log.WriteLine("[InfStamina] Enabled");
                    };
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[InfStamina]: {ex.Message}");
                ClearCache();
            }
        }

        private (ulong stamina, ulong oxygen) GetStaminaObjects(LocalPlayer localPlayer)
        {
            if (_cachedStaminaObj.IsValidVirtualAddress() && _cachedOxygenObj.IsValidVirtualAddress())
                return (_cachedStaminaObj, _cachedOxygenObj);

            var physical = GetPhysical(localPlayer);
            if (!physical.IsValidVirtualAddress()) return (0, 0);

            var stamina = Memory.ReadPtr(physical + Offsets.Physical.Stamina);
            var oxygen  = Memory.ReadPtr(physical + Offsets.Physical.Oxygen);

            if (stamina.IsValidVirtualAddress()) _cachedStaminaObj = stamina;
            if (oxygen.IsValidVirtualAddress())  _cachedOxygenObj  = oxygen;

            return (stamina, oxygen);
        }

        private ulong GetPhysical(LocalPlayer localPlayer)
        {
            if (_cachedPhysical.IsValidVirtualAddress()) return _cachedPhysical;
            var phys = Memory.ReadPtr(localPlayer.Base + Offsets.Player.Physical);
            if (phys.IsValidVirtualAddress()) _cachedPhysical = phys;
            return phys;
        }

        private void ClearCache()
        {
            _cachedPhysical  = default;
            _cachedStaminaObj = default;
            _cachedOxygenObj  = default;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            ClearCache();
        }

        public override void OnRaidEnd() => ClearCache();
    }
}
