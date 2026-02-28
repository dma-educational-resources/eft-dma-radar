using System;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_shared.Common.Unity;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Common.DMA.ScatterAPI;
using HandyControl.Controls;
using eft_dma_radar.Common.DMA.Features;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class MoveSpeed : MemWriteFeature<MoveSpeed>
    {
        private const float BASE_SPEED = 1.0f;
        private const float WEIGHT_LIMIT = 39.8f;
        private const float SPEED_TOLERANCE = 0.1f;

        private float _lastSpeed;
        private bool _lastEnabledState;
        private bool _lastOverweightState;
        private ulong _cachedAnimator;

        public override bool Enabled
        {
            get => MemWrites.Config.MoveSpeed.Enabled;
            set => MemWrites.Config.MoveSpeed.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                var configSpeed = MemWrites.Config.MoveSpeed.Multiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var speedChanged = Math.Abs(_lastSpeed - configSpeed) > SPEED_TOLERANCE;

                var animator = GetAnimator(localPlayer);
                if (!animator.IsValidVirtualAddress())
                    return;

                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                // Weight check
                // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
                var physical = Memory.ReadPtr(localPlayer + Offsets.Player.Physical);
                if (!physical.IsValidVirtualAddress())
                    return;

                float weightKg = Memory.ReadValue<float>(
                    physical + Offsets.Physical.PreviousWeight,
                    false
                );

                bool overweight = weightKg >= WEIGHT_LIMIT;

                if (overweight && !_lastOverweightState && Enabled)
                {
                    XMLogging.WriteLine(
                        $"[MoveSpeed] You are too FAT! Reducing MoveSpeed (Weight={weightKg:F1}kg)"
                    );
                    //NotificationsShared.InfoExtended(
                    //    "Move Speed",
                    //    "You are carrying too much weight!\n" +
                    //    "Move Speed has been reduced to normal speed."
                    //);
                }

                float targetSpeed =
                    overweight ? BASE_SPEED :
                    Enabled    ? configSpeed :
                                 BASE_SPEED;

                float currentSpeed = Memory.ReadValue<float>(
                    animator + UnityOffsets.UnityAnimator.Speed,
                    false
                );

                if (Math.Abs(currentSpeed - targetSpeed) <= SPEED_TOLERANCE &&
                    !stateChanged && !speedChanged)
                    return;

                ValidateSpeed(currentSpeed, targetSpeed);

                writes.AddValueEntry(
                    animator + UnityOffsets.UnityAnimator.Speed,
                    targetSpeed
                );

                writes.Callbacks += () =>
                {
                    _lastEnabledState = Enabled;
                    _lastSpeed = configSpeed;
                    _lastOverweightState = overweight;

                    XMLogging.WriteLine(
                        $"[MoveSpeed] {(Enabled ? "Enabled" : "Disabled")} | " +
                        $"Weight={weightKg:F1}kg | Speed={targetSpeed:F2}"
                    );
                };
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[MoveSpeed]: {ex}");
                _cachedAnimator = default;
            }
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (_cachedAnimator.IsValidVirtualAddress())
                return _cachedAnimator;

            var pAnimators = Memory.ReadPtr(localPlayer + Offsets.Player._animators);
            if (!pAnimators.IsValidVirtualAddress())
                return 0x0;

            using var animators = MemArray<ulong>.Get(pAnimators);
            if (animators == null || animators.Count == 0)
                return 0x0;

            var animator = Memory.ReadPtrChain(
                animators[0],
                new uint[]
                {
                    Offsets.BodyAnimator.UnityAnimator,
                    ObjectClass.MonoBehaviourOffset
                }
            );

            if (!animator.IsValidVirtualAddress())
                return 0x0;

            _cachedAnimator = animator;
            return animator;
        }

        private static void ValidateSpeed(float currentSpeed, float targetSpeed)
        {
            if (!float.IsNormal(currentSpeed) ||
                currentSpeed < BASE_SPEED - 0.3f ||
                currentSpeed > Math.Max(targetSpeed, BASE_SPEED) + 0.3f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentSpeed),
                    $"Invalid animator speed: {currentSpeed:F2}"
                );
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = default;
            _lastSpeed = default;
            _lastOverweightState = default;
            _cachedAnimator = default;
        }
    }
}
