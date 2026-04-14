using eft_dma_radar.Silk.DMA.Features;
using eft_dma_radar.Silk.DMA.ScatterAPI;

namespace eft_dma_radar.Silk.Tarkov.Features.MemoryWrites
{
    public sealed class WideLean : MemWriteFeature<WideLean>
    {
        public static EWideLeanDirection Direction = EWideLeanDirection.Off;
        private bool _set;
        private static readonly Vector3 OFF = Vector3.Zero;

        public override bool Enabled
        {
            get => SilkProgram.Config.MemWrites.WideLean.Enabled;
            set => SilkProgram.Config.MemWrites.WideLean.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is not LocalPlayer localPlayer)
                    return;

                if (!localPlayer.PWA.IsValidVirtualAddress())
                    return;

                var dir = Direction;
                if (Enabled && dir is not EWideLeanDirection.Off && !_set)
                {
                    var amt = SilkProgram.Config.MemWrites.WideLean.Amount * 0.2f;

                    var vec = dir switch
                    {
                        EWideLeanDirection.Left => new Vector3(-amt, 0f, 0f),
                        EWideLeanDirection.Right => new Vector3(amt, 0f, 0f),
                        EWideLeanDirection.Up => new Vector3(0f, 0f, amt),
                        _ => throw new InvalidOperationException("Invalid wide lean option"),
                    };

                    writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, vec);
                    writes.Callbacks += () =>
                    {
                        _set = true;
                        Log.WriteLine("[WideLean] On");
                    };
                }
                else if (_set && dir is EWideLeanDirection.Off)
                {
                    var off = OFF;
                    writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, off);
                    writes.Callbacks += () =>
                    {
                        _set = false;
                        Log.WriteLine("[WideLean] Off");
                    };
                }
            }
            catch (Exception ex)
            {
                Direction = EWideLeanDirection.Off;
                Log.WriteLine($"[WideLean]: {ex.Message}");
            }
        }

        public override void OnRaidStart()
        {
            _set = default;
        }

        public enum EWideLeanDirection
        {
            Off,
            Left,
            Right,
            Up
        }
    }
}
