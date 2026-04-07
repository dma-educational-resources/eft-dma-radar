namespace eft_dma_radar.Silk.Misc.Pools
{
    /// <summary>
    /// Object pooling interface. Implement on classes you want rented from a pool.
    /// Guidelines:
    /// (1) Apply IPooledObject{T} to the class.
    /// (2) Provide a public parameterless constructor (mark [Obsolete]).
    /// (3) Dispose() must call IPooledObject{T}.Return(this).
    /// (4) SetDefault() must fully reset state.
    /// </summary>
    public interface IPooledObject<T> : IDisposable
        where T : class, IPooledObject<T>
    {
        void SetDefault();

        static T Rent() => ObjectPool.Rent();
        static void Return(T obj)
        {
            if (obj is IPooledObject<T> p)
            {
                p.SetDefault();
                ObjectPool.Return(obj);
            }
            else
            {
                Log.WriteLine($"CRITICAL: Cannot return '{obj.GetType()}' to pool.");
            }
        }

        private static class ObjectPool
        {
            private const int MaxPoolSize = 256;
            private static readonly ConcurrentStack<T> _pool = new();
            private static readonly Func<T> _factory =
                System.Linq.Expressions.Expression.Lambda<Func<T>>(
                    System.Linq.Expressions.Expression.New(typeof(T))).Compile();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static T Rent() =>
                _pool.TryPop(out var obj) ? obj : _factory();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static void Return(T obj)
            {
                if (_pool.Count < MaxPoolSize)
                    _pool.Push(obj);
            }
        }
    }
}
