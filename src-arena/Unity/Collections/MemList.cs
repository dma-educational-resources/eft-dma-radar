namespace eft_dma_radar.Arena.Unity.Collections
{
    public sealed class MemList<T> : SharedArray<T>, IPooledObject<MemList<T>>
        where T : unmanaged
    {
        public const uint CountOffset   = 0x18;
        public const uint ArrOffset     = 0x10;
        public const uint ArrStartOffset = 0x20;

        public static MemList<T> Get(ulong addr, bool useCache = true)
        {
            var list = IPooledObject<MemList<T>>.Rent();
            list.Initialize(addr, useCache);
            return list;
        }

        private void Initialize(ulong addr, bool useCache = true)
        {
            try
            {
                var count = Memory.ReadValue<int>(addr + CountOffset, useCache);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
                Initialize(count);
                if (count == 0) return;
                var listBase = Memory.ReadPtr(addr + ArrOffset, useCache) + ArrStartOffset;
                Memory.ReadBuffer(listBase, Span, useCache);
            }
            catch { Dispose(); throw; }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemList() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemList<T>>.Return(this);
        }
    }
}
