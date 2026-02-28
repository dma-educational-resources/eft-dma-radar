using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc.Pools;

namespace eft_dma_radar.Common.Unity.Collections
{
    /// <summary>
    /// DMA Wrapper for a C# Array
    /// Must initialize before use. Must dispose after use.
    /// </summary>
    /// <typeparam name="T">Array Type</typeparam>
    public sealed class MemArray<T> : SharedArray<T>, IPooledObject<MemArray<T>>
        where T : unmanaged
    {
        public const uint CountOffset = 0x18;
        public const uint ArrBaseOffset = 0x20;

        /// <summary>
        /// Get a MemArray <typeparamref name="T"/> from the object pool.
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="useCache">Perform cached reading.</param>
        /// <returns>Rented MemArray <typeparamref name="T"/> instance.</returns>
        public static MemArray<T> Get(ulong addr, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, useCache);
            return arr;
        }

        /// <summary>
        /// Get a MemArray <typeparamref name="T"/> from the object pool.
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="count">Number of elements in array.</param>
        /// <param name="useCache">Perform cached reading.</param>
        /// <returns>Rented MemArray <typeparamref name="T"/> instance.</returns>
        public static MemArray<T> Get(ulong addr, int count, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, count, useCache);
            return arr;
        }

        /// <summary>
        /// Initializer for Unity Array
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
                Memory.ReadBuffer(addr + ArrBaseOffset, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Initializer for Raw Memory Array.
        /// Static defined count.
        /// Reading begins at addr + 0x0
        /// </summary>
        /// <param name="addr">Base Address for this collection.</param>
        /// <param name="count">Number of elements in array.</param>
        /// <param name="useCache">Perform cached reading.</param>
        private void Initialize(ulong addr, int count, bool useCache = true)
        {
            try
            {
                Initialize(count);
                if (count == 0)
                    return;
                Memory.ReadBuffer(addr, Span, useCache);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemArray() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemArray<T>>.Return(this);
        }
    }
}
