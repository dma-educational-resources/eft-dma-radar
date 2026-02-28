using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class DisableHeadBobbing : MemWriteFeature<DisableHeadBobbing>
    {
        private bool _lastEnabledState;
        private ulong _cachedValuePtr;

        private const float DEFAULT_VALUE   = 0.2f;
        private const float DISABLED_VALUE  = 0f;

        public override bool Enabled
        {
            get => MemWrites.Config.DisableHeadBobbing;
            set => MemWrites.Config.DisableHeadBobbing = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                // Only do work when the toggle changes
                var stateChanged = Enabled != _lastEnabledState;
                if (!stateChanged)
                    return;

                var valuePtr = GetHeadBobbingValuePtr();
                if (!valuePtr.IsValidVirtualAddress())
                    return;

                // If Enabled ¡ú set to 0, else ¡ú restore default
                float targetValue = Enabled ? DISABLED_VALUE : DEFAULT_VALUE;

                writes.AddValueEntry(valuePtr + Offsets.BSGGameSettingValueClass.Value, targetValue);

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    XMLogging.WriteLine($"[DisableHeadBobbing] {(Enabled ? "Enabled" : "Disabled")} ¡ú {targetValue}");
                };
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableHeadBobbing] ERROR: {ex}");
                _cachedValuePtr = 0;
            }
        }

        /// <summary>
        /// Resolve STATIC FIELD: EFT.Settings.Game.GameSettingsGroup.HeadBobbing
        /// </summary>
        private ulong GetHeadBobbingValuePtr()
        {
            if (_cachedValuePtr.IsValidVirtualAddress())
                return _cachedValuePtr;

            try
            {
                // 1. Locate IL2CPP class
                var klass = Il2CppClass.Find(
                    "Assembly-CSharp",
                    "EFT.Settings.Game.GameSettingsGroup",
                    out var klassPtr);

                if (!klassPtr.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[DisableHeadBobbing] Could not resolve GameSettingsGroup class");
                    return 0;
                }

                // 2. Resolve static field block (this is where all fields live)
                ulong staticFieldData = Il2CppClass.GetStaticFieldData(klassPtr);
                if (!staticFieldData.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[DisableHeadBobbing] Class has no static field block?");
                    return 0;
                }

                // 3. HeadBobbing object pointer = staticFieldData + 0x68
                ulong headBobbingPtr = Memory.ReadPtr(staticFieldData + 0x68);
                if (!headBobbingPtr.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[DisableHeadBobbing] HeadBobbing object missing");
                    return 0;
                }

                // 4. Setting<T>.ValueClass
                ulong valueClass = Memory.ReadPtr(headBobbingPtr + Offsets.BSGGameSetting.ValueClass);
                if (!valueClass.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[DisableHeadBobbing] valueClass missing");
                    return 0;
                }

                _cachedValuePtr = valueClass;
                XMLogging.WriteLine($"[DisableHeadBobbing] Cached value ptr: 0x{valueClass:X}");
                return valueClass;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DisableHeadBobbing] Resolve failed: {ex}");
                return 0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedValuePtr   = 0;
        }
    }
}
