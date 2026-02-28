using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc.Pools;

namespace eft_dma_radar.Common.Unity.Collections
{
    /// <summary>
    /// DMA Wrapper for a C# List
    /// Must initialize before use. Must dispose after use.
    /// </summary>
    /// <typeparam name="T">Collection Type</typeparam>
    public sealed class MemList<T> : SharedArray<T>, IPooledObject<MemList<T>>
        where T : unmanaged
    {
        public const uint CountOffset = 0x18;
        public const uint ArrOffset = 0x10;
        public const uint ArrStartOffset = 0x20;

        /// <summary>
        /// Get a MemList <typeparamref name="T"/> from the object pool.
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="useCache">Perform cached reading.</param>
        /// <returns>Rented MemList <typeparamref name="T"/> instance.</returns>
        public static MemList<T> Get(ulong addr, bool useCache = true)
        {
            var list = IPooledObject<MemList<T>>.Rent();
            list.Initialize(addr, useCache);
            return list;
        }

        /// <summary>
        /// Initializer for Unity List
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
                var listBase = Memory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                Memory.ReadBuffer(listBase, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemList() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemList<T>>.Return(this);
        }
    }
}
