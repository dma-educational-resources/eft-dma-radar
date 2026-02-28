using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Features;

using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.UI.Misc;
using static eft_dma_radar.Common.Misc.InputManager;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = eft_dma_radar.Common.Misc.InputManager.KeyEventArgs;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites
{
    public sealed class WideLean : MemWriteFeature<WideLean>
    {
        public static EWideLeanDirection Direction = EWideLeanDirection.Off;
        private bool _set = false;
        private Vector3 OFF = Vector3.Zero;

        public override bool Enabled
        {
            get => MemWrites.Config.WideLean.Enabled;
            set => MemWrites.Config.WideLean.Enabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// Wide Lean Config.
        /// </summary>
        public static WideLeanConfig Config { get; } = MemWrites.Config.WideLean;

        public override void TryApply(ScatterWriteHandle writes)
        {
            try
            {
                if (Memory.LocalPlayer is LocalPlayer localPlayer)
                {
                    var dir = Direction;
                    if (Enabled && dir is not EWideLeanDirection.Off && !_set)
                    {
                        var amt = Config.Amount * 0.2f;

                        switch (dir)
                        {
                            case EWideLeanDirection.Left:
                                var left = new Vector3(-amt, 0f, 0f);
                                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, ref left);
                                break;
                            case EWideLeanDirection.Right:
                                var right = new Vector3(amt, 0f, 0f);
                                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, ref right);
                                break;
                            case EWideLeanDirection.Up:
                                var up = new Vector3(0f, 0f, amt);
                                writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, ref up);
                                break;
                            default:
                                throw new InvalidOperationException("Invalid wide lean option");
                        }
                        writes.Callbacks += () =>
                        {
                            _set = true;
                            XMLogging.WriteLine("Wide Lean [On]");
                        };
                    }
                    else if (_set && dir is EWideLeanDirection.Off)
                    {
                        writes.AddValueEntry(localPlayer.PWA + Offsets.ProceduralWeaponAnimation.PositionZeroSum, ref OFF);
                        writes.Callbacks += () =>
                        {
                            _set = false;
                            XMLogging.WriteLine("Wide Lean [Off]");
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Direction = EWideLeanDirection.Off;
                XMLogging.WriteLine($"Wide Lean [ERROR] {ex}");
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
