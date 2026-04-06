namespace eft_dma_radar.Silk.Misc.Pools
{
    /// <summary>
    /// Pooled array backed by ArrayPool{T}.Shared.
    /// Always call Dispose() when done.
    /// </summary>
    public class SharedArray<T> : IEnumerable<T>, IDisposable, IPooledObject<SharedArray<T>>
        where T : unmanaged
    {
        private T[]? _arr;

        public Span<T> Span => _arr.AsSpan(0, Count);
        public ReadOnlySpan<T> ReadOnlySpan => _arr.AsSpan(0, Count);
        public int Count { get; private set; }
        public ref T this[int i] => ref Span[i];

        [Obsolete("Rent via SharedArray<T>.Get(count).")]
        public SharedArray() { }

        public static SharedArray<T> Get(int count)
        {
            var arr = IPooledObject<SharedArray<T>>.Rent();
            try { arr.Initialize(count); return arr; }
            catch { arr.Dispose(); throw; }
        }

        protected void Initialize(int count)
        {
            if (_arr is not null) throw new InvalidOperationException("Already initialized.");
            Count = count;
            _arr = ArrayPool<T>.Shared.Rent(count);
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return _arr![i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void SetDefault()
        {
            if (_arr is not null)
            {
                ArrayPool<T>.Shared.Return(_arr);
                _arr = null;
            }
            Count = 0;
        }

        protected virtual void Dispose(bool disposing) => IPooledObject<SharedArray<T>>.Return(this);
        public void Dispose() => Dispose(true);
    }
}
