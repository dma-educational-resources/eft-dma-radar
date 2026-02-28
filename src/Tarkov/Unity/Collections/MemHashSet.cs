using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc.Pools;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Common.Unity.Collections
{
    /// <summary>
    /// DMA Wrapper for a C# HashSet
    /// Must initialize before use. Must dispose after use.
    /// </summary>
    /// <typeparam name="T">Collection Type</typeparam>
    public sealed class MemHashSet<T> : SharedArray<MemHashSet<T>.MemHashEntry>, IPooledObject<MemHashSet<T>>
        where T : unmanaged
    {
        public const uint CountOffset = 0x38;
        public const uint ArrOffset = 0x18;
        public const uint ArrStartOffset = 0x20;

        /// <summary>
        /// Get a MemHashSet <typeparamref name="T"/> from the object pool.
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="useCache">Perform cached reading.</param>
        /// <returns>Rented MemHashSet <typeparamref name="T"/> instance.</returns>
        public static MemHashSet<T> Get(ulong addr, bool useCache = true)
        {
            var hashSet = IPooledObject<MemHashSet<T>>.Rent();
            hashSet.Initialize(addr, useCache);
            return hashSet;
        }

        /// <summary>
        /// Initializer for Unity HashSet
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="useCache">Perform cached reading.</param>
        private void Initialize(ulong addr, bool useCache = true)
        {
            try
            {
                var count = Memory.ReadValue<int>(addr + CountOffset, useCache);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
                Initialize(count);
                if (count == 0)
                    return;
                var hashSetBase = Memory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                Memory.ReadBuffer(hashSetBase, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemHashSet() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemHashSet<T>>.Return(this);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public readonly struct MemHashEntry
        {
            public static implicit operator T(MemHashEntry x) => x.Value;

            private readonly int _hashCode;
            private readonly int _next;
            public readonly T Value;
        }
    }
}
