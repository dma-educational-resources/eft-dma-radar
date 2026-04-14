using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class NoRecoil : MemWriteFeature<NoRecoil>
    {
        private float _lastRecoil;
        private float _lastSway;
        private ulong _cachedBreathEffector;
        private ulong _cachedShotEffector;
        private ulong _cachedNewShotRecoil;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.NoRecoil;
            set => SilkProgram.Config.MemWrites.NoRecoil = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(50);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer || !localPlayer.PWA.IsValidVirtualAddress())
                    return;

                var (breathEffector, shotEffector, newShotRecoil) = GetEffectorPointers(localPlayer);
                if (!breathEffector.IsValidVirtualAddress() ||
                    !shotEffector.IsValidVirtualAddress() ||
                    !newShotRecoil.IsValidVirtualAddress())
                    return;

                var recoilAmt = Enabled ? SilkProgram.Config.MemWrites.NoRecoilAmount * 0.01f : 1.0f;
                var swayAmt   = Enabled ? SilkProgram.Config.MemWrites.NoSwayAmount   * 0.01f : 1.0f;

                if (!Enabled && _lastRecoil == 1.0f && _lastSway == 1.0f)
                    return;

                var breathCurrent = Memory.ReadValue<float>(breathEffector + Offsets.BreathEffector.Intensity, false);
                var shotCurrent   = Memory.ReadValue<Vector3>(newShotRecoil + Offsets.NewShotRecoil.IntensitySeparateFactors, false);
                var mask          = Memory.ReadValue<int>(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.Mask, false);

                if (Math.Abs(breathCurrent - swayAmt) > 0.001f)
                    writes.AddValueEntry(breathEffector + Offsets.BreathEffector.Intensity, swayAmt);

                var targetVec = new Vector3(recoilAmt, recoilAmt, recoilAmt);
                if (shotCurrent != targetVec)
                    writes.AddValueEntry(newShotRecoil + Offsets.NewShotRecoil.IntensitySeparateFactors, targetVec);

                writes.Callbacks += () =>
                {
                    _lastRecoil = recoilAmt;
                    _lastSway = swayAmt;
                };
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[NoRecoil]: {ex.Message}");
                ClearCache();
            }
        }

        private (ulong breath, ulong shot, ulong newShot) GetEffectorPointers(LocalPlayer localPlayer)
        {
            if (_cachedBreathEffector.IsValidVirtualAddress() &&
                _cachedShotEffector.IsValidVirtualAddress() &&
                _cachedNewShotRecoil.IsValidVirtualAddress())
                return (_cachedBreathEffector, _cachedShotEffector, _cachedNewShotRecoil);

            var pwa = localPlayer.PWA;
            if (!pwa.IsValidVirtualAddress()) return (0, 0, 0);

            var breath = Memory.ReadPtr(pwa + Offsets.ProceduralWeaponAnimation.Breath);
            var shot   = Memory.ReadPtr(pwa + Offsets.ProceduralWeaponAnimation.Shootingg);
            if (!breath.IsValidVirtualAddress() || !shot.IsValidVirtualAddress()) return (0, 0, 0);

            var newShot = Memory.ReadPtr(shot + Offsets.ShotEffector.NewShotRecoil);
            if (!newShot.IsValidVirtualAddress()) return (0, 0, 0);

            _cachedBreathEffector = breath;
            _cachedShotEffector   = shot;
            _cachedNewShotRecoil  = newShot;
            return (breath, shot, newShot);
        }

        private void ClearCache()
        {
            _cachedBreathEffector = default;
            _cachedShotEffector   = default;
            _cachedNewShotRecoil  = default;
        }

        public override void OnRaidStart()
        {
            _lastRecoil = default;
            _lastSway   = default;
            ClearCache();
        }

        public override void OnRaidEnd() => ClearCache();
    }
}
