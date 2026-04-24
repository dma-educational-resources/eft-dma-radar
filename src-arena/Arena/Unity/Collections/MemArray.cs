namespace eft_dma_radar.Arena.Unity.Collections
{
    public sealed class MemArray<T> : SharedArray<T>, IPooledObject<MemArray<T>>
        where T : unmanaged
    {
        public const uint CountOffset  = 0x18;
        public const uint ArrBaseOffset = 0x20;

        public static MemArray<T> Get(ulong addr, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, useCache);
            return arr;
        }

        public static MemArray<T> Get(ulong addr, int count, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, count, useCache);
            return arr;
        }

        private void Initialize(ulong addr, bool useCache = true)
        {
            try
            {
                var count = Memory.ReadValue<int>(addr + CountOffset, useCache);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
                Initialize(count);
                if (count == 0) return;
                Memory.ReadBuffer(addr + ArrBaseOffset, Span, useCache);
            }
            catch { Dispose(); throw; }
        }

        private void Initialize(ulong addr, int count, bool useCache = true)
        {
            try
            {
                Initialize(count);
                if (count == 0) return;
                Memory.ReadBuffer(addr, Span, useCache);
            }
            catch { Dispose(); throw; }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemArray() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemArray<T>>.Return(this);
        }
    }
}
