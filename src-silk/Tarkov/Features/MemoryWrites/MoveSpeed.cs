using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;
using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class MoveSpeed : MemWriteFeature<MoveSpeed>
    {
        private const float BASE_SPEED    = 1.0f;
        private const float WEIGHT_LIMIT  = 39.8f;
        private const float SPEED_TOLERANCE = 0.1f;

        private float _lastSpeed;
        private bool  _lastEnabledState;
        private bool  _lastOverweightState;
        private ulong _cachedAnimator;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.MoveSpeed.Enabled;
            set => SilkProgram.Config.MemWrites.MoveSpeed.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var configSpeed  = SilkProgram.Config.MemWrites.MoveSpeed.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var speedChanged = Math.Abs(_lastSpeed - configSpeed) > SPEED_TOLERANCE;

                var animator = GetAnimator(localPlayer);
                if (!animator.IsValidVirtualAddress())
                    return;

                var physical = Memory.ReadPtr(localPlayer.Base + Offsets.Player.Physical);
                if (!physical.IsValidVirtualAddress())
                    return;

                float weightKg = Memory.ReadValue<float>(physical + Offsets.Physical.PreviousWeight, false);
                bool overweight = weightKg >= WEIGHT_LIMIT;

                float targetSpeed = overweight ? BASE_SPEED : Enabled ? configSpeed : BASE_SPEED;

                float currentSpeed = Memory.ReadValue<float>(
                    animator + UnityAnimator.Speed, false);

                if (Math.Abs(currentSpeed - targetSpeed) <= SPEED_TOLERANCE && !stateChanged && !speedChanged)
                    return;

                writes.AddValueEntry(animator + UnityAnimator.Speed, targetSpeed);

                bool enableSnapshot   = Enabled;
                bool overweightSnap   = overweight;
                writes.Callbacks += () =>
                {
                    _lastEnabledState    = enableSnapshot;
                    _lastSpeed           = configSpeed;
                    _lastOverweightState = overweightSnap;
                    Log.WriteLine($"[MoveSpeed] {(enableSnapshot ? "Enabled" : "Disabled")} | Weight={weightKg:F1}kg | Speed={targetSpeed:F2}");
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[MoveSpeed]: {ex.Message}");
                _cachedAnimator = default;
            }
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (_cachedAnimator.IsValidVirtualAddress())
                return _cachedAnimator;

            var pAnimators = Memory.ReadPtr(localPlayer.Base + Offsets.Player._animators);
            if (!pAnimators.IsValidVirtualAddress()) return 0;

            using var animators = MemArray<ulong>.Get(pAnimators);
            if (animators == null || animators.Count == 0) return 0;

            var animator = Memory.ReadPtrChain(
                animators[0],
                new uint[] { Offsets.BodyAnimator.UnityAnimator, ObjectClass.MonoBehaviourOffset });

            if (!animator.IsValidVirtualAddress()) return 0;

            _cachedAnimator = animator;
            return animator;
        }

        public override void OnRaidEnd()
        {
            _lastEnabledState    = default;
            _lastSpeed           = default;
            _lastOverweightState = default;
            _cachedAnimator      = default;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState    = default;
            _lastSpeed           = default;
            _lastOverweightState = default;
            _cachedAnimator      = default;
        }
    }
}
