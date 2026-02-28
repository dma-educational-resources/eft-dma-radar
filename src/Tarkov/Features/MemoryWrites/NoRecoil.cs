using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class NoRecoil : MemWriteFeature<NoRecoil>
    {
        private float _lastRecoil;
        private float _lastSway;
        private ulong _cachedBreathEffector;
        private ulong _cachedShotEffector;
        private ulong _cachedNewShotRecoil;

        private const Enums.EProceduralAnimationMask ORIGINAL_PWA_MASK =
            Enums.EProceduralAnimationMask.MotionReaction |
            Enums.EProceduralAnimationMask.ForceReaction |
            Enums.EProceduralAnimationMask.Shooting |
            Enums.EProceduralAnimationMask.DrawDown |
            Enums.EProceduralAnimationMask.Aiming |
            Enums.EProceduralAnimationMask.Breathing;

        public override bool Enabled
        {
            get => MemWrites.Config.NoRecoil;
            set => MemWrites.Config.NoRecoil = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(50);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer || !Enabled)
                    return;

                var (breathEffector, shotEffector, newShotRecoil) = GetEffectorPointers(localPlayer);

                if (!ValidatePointers(breathEffector, shotEffector, newShotRecoil))
                    return;

                var enabled = Enabled;
                var recoilAmt = MemWrites.Config.NoRecoilAmount * 0.01f;
                var newSway = MemWrites.Config.NoSwayAmount * 0.01f;

                if (MemWriteFeature<RageMode>.Instance.Enabled)
                {
                    enabled = true;
                    recoilAmt = 0f;
                    newSway = 0f;
                }

                if (!enabled)
                {
                    if (_lastRecoil == 1.0f && _lastSway == 1.0f)
                        return;

                    recoilAmt = 1.0f;
                    newSway = 1.0f;
                }

                var breathAmt = Memory.ReadValue<float>(breathEffector + Offsets.BreathEffector.Intensity, false);
                var shotAmt = Memory.ReadValue<Vector3>(newShotRecoil + Offsets.NewShotRecoil.IntensitySeparateFactors, false);
                var mask = Memory.ReadValue<int>(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.Mask, false);

                ValidateSway(breathAmt);
                ValidateRecoil(shotAmt);
                ValidateMask(mask);

                if (Math.Abs(breathAmt - newSway) > 0.001f)
                {
                    writes.AddValueEntry(breathEffector + Offsets.BreathEffector.Intensity, newSway);
                    writes.Callbacks += () => XMLogging.WriteLine($"[NoRecoil] BreathEffector {breathAmt:F3} -> {newSway:F3}");
                }

                var recoilAmtVec = new Vector3(recoilAmt, recoilAmt, recoilAmt);

                if (shotAmt != recoilAmtVec)
                {
                    writes.AddValueEntry(newShotRecoil + Offsets.NewShotRecoil.IntensitySeparateFactors, recoilAmtVec);
                    writes.Callbacks += () => XMLogging.WriteLine($"[NoRecoil] ShotEffector {shotAmt} -> {recoilAmtVec}");
                }

                var resetMask = (newSway > 0 && _lastSway == 0) || (recoilAmt > 0 && _lastRecoil == 0);
                
                if (resetMask)
                    WriteMask((int)ORIGINAL_PWA_MASK, mask);
                else if (recoilAmt == 0 && newSway == 0 && mask != 1)
                    WriteMask(1, mask);

                _lastRecoil = recoilAmt;
                _lastSway = newSway;

                void WriteMask(int newMask, int currentMask)
                {
                    writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.Mask, newMask);
                    writes.Callbacks += () => XMLogging.WriteLine($"[NoRecoil] Mask {currentMask} -> {newMask}");
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[NoRecoil]: {ex}");
                ClearCache();
            }
        }

        private (ulong breathEffector, ulong shotEffector, ulong newShotRecoil) GetEffectorPointers(LocalPlayer localPlayer)
        {
            if (_cachedBreathEffector.IsValidVirtualAddress() &&
                _cachedShotEffector.IsValidVirtualAddress() &&
                _cachedNewShotRecoil.IsValidVirtualAddress())
            {
                return (_cachedBreathEffector, _cachedShotEffector, _cachedNewShotRecoil);
            }

            var PWA = localPlayer.PWA;
            if (!PWA.IsValidVirtualAddress())
                return (0x0, 0x0, 0x0);

            var breathEffector = Memory.ReadPtr(PWA + Offsets.ProceduralWeaponAnimation.Breath);
            var shotEffector = Memory.ReadPtr(PWA + Offsets.ProceduralWeaponAnimation.Shootingg);

            if (!breathEffector.IsValidVirtualAddress() || !shotEffector.IsValidVirtualAddress())
                return (0x0, 0x0, 0x0);

            var newShotRecoil = Memory.ReadPtr(shotEffector + Offsets.ShotEffector.NewShotRecoil);
            if (!newShotRecoil.IsValidVirtualAddress())
                return (0x0, 0x0, 0x0);

            _cachedBreathEffector = breathEffector;
            _cachedShotEffector = shotEffector;
            _cachedNewShotRecoil = newShotRecoil;

            return (breathEffector, shotEffector, newShotRecoil);
        }

        private static bool ValidatePointers(ulong breathEffector, ulong shotEffector, ulong newShotRecoil)
        {
            return breathEffector.IsValidVirtualAddress() &&
                   shotEffector.IsValidVirtualAddress() &&
                   newShotRecoil.IsValidVirtualAddress();
        }

        private void ClearCache()
        {
            _cachedBreathEffector = default;
            _cachedShotEffector = default;
            _cachedNewShotRecoil = default;
        }

        public override void OnRaidStart()
        {
            _lastRecoil = default;
            _lastSway = default;
            ClearCache();
        }

        public override void OnRaidEnd()
        {
            _lastRecoil = default;
            _lastSway = default;
            ClearCache();
        }

        private static void ValidateSway(float amount)
        {
            if (amount < 0f || amount > 5f)
                throw new ArgumentOutOfRangeException(nameof(amount), $"Sway amount {amount} is out of valid range [0, 5]");
        }

        private static void ValidateRecoil(Vector3 amount)
        {
            if (amount.X < 0f || amount.X > 1f ||
                amount.Y < 0f || amount.Y > 1f ||
                amount.Z < 0f || amount.Z > 1f)
                throw new ArgumentOutOfRangeException(nameof(amount), $"Recoil amount {amount} is out of valid range [0, 1]");
        }

        private static void ValidateMask(int mask)
        {
            if (mask < 0 || mask > 256)
                throw new ArgumentOutOfRangeException(nameof(mask), $"Mask {mask} is out of valid range [0, 256]");
        }
    }
}