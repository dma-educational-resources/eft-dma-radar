
using eft_dma_shared.Common.Unity.LowLevel.Hooks;

namespace eft_dma_radar.Common.Unity.LowLevel
{
    public readonly struct Behaviour
    {
        public static implicit operator ulong(Behaviour x) => x.Base;
        private readonly ulong Base;

        public Behaviour(ulong baseAddress)
        {
            Base = baseAddress;
        }

        public readonly bool GetState() => Memory.ReadValue<bool>(this + UnityOffsets.Behaviour.IsEnabled);
        public readonly bool SetState(bool newState)
        {
            var result = NativeMethods.SetBehaviorState(Base, newState);
            return result != 0;
        }
    }
}