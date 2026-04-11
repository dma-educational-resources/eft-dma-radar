namespace eft_dma_radar.Silk.DMA.ScatterAPI
{
    /// <summary>
    /// Represents a 64-bit virtual pointer address. Implicitly casts to/from ulong.
    /// </summary>
    public readonly struct MemPointer
    {
        public static implicit operator MemPointer(ulong x) => x;
        public static implicit operator ulong(MemPointer x) => x._pointer;

#pragma warning disable CS0649
        private readonly ulong _pointer;
#pragma warning restore CS0649

        public override string ToString() => _pointer.ToString("X");
    }
}
