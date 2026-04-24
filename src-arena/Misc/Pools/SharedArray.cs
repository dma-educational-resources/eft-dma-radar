namespace eft_dma_radar.Arena.Misc.Pools
{
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

        public Enumerator GetEnumerator() => new(_arr!, Count);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[] _arr;
            private readonly int _count;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(T[] arr, int count) { _arr = arr; _count = count; _index = -1; }

            public readonly T Current { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _arr[_index]; }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => ++_index < _count;
            public void Reset() => _index = -1;
            readonly object System.Collections.IEnumerator.Current => Current;
            public readonly void Dispose() { }
        }

        public void SetDefault()
        {
            if (_arr is not null) { ArrayPool<T>.Shared.Return(_arr); _arr = null; }
            Count = 0;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) IPooledObject<SharedArray<T>>.Return(this);
        }

        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}
