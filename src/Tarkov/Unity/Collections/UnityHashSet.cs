using Collections.Pooled;

namespace eft_dma_radar.Common.Unity.Collections
{
    /// <summary>
    /// DMA Wrapper for a C# HashSet
    /// Must initialize before use. Must dispose after use.
    /// </summary>
    /// <typeparam name="T">Collection Type</typeparam>
    public sealed class UnityHashSet<T> : PooledMemory<UnityHashSet<T>.MemHashEntry>
        where T : unmanaged
    {
        public const uint CountOffset = 0x38;
        public const uint ArrOffset = 0x18;
        public const uint ArrStartOffset = 0x20;

        private UnityHashSet() : base(0) { }
        private UnityHashSet(int count) : base(count) { }

        /// <summary>
        /// Factory method to create a new <see cref="UnityHashSet{T}"/> instance from a memory address.
        /// </summary>
        /// <param name="addr"></param>
        /// <param name="useCache"></param>
        /// <returns></returns>
        public static UnityHashSet<T> Create(ulong addr, bool useCache = true)
        {
            var count = Tarkov.MemoryInterface.Memory.ReadValue<int>(addr + CountOffset, useCache);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
            var hs = new UnityHashSet<T>(count);
            try
            {
                if (count == 0)
                {
                    return hs;
                }
                var hashSetBase = Tarkov.MemoryInterface.Memory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                Tarkov.MemoryInterface.Memory.ReadBuffer(hashSetBase, hs.Span, useCache);
                return hs;
            }
            catch
            {
                hs.Dispose();
                throw;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public readonly struct MemHashEntry
        {
            public static implicit operator T(MemHashEntry x) => x.Value;

            private readonly int _hashCode;
            private readonly int _next;
            public readonly T Value;
        }
    }
}
