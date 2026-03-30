using eft_dma_radar.Common.Misc;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace eft_dma_radar.Common.Misc.Pools
{
    /// <summary>
    /// Interface devinces a class object that can be rented from an Object Pool for improved allocation performance.
    /// Guidelines:
    /// (1) Apply the IPooledObject<typeparamref name="T"/> interface to the class object.
    /// (2) The class must contain a parameterless public constructor. It's recommended to set [Obsolete] parameter on this constructor, and use a static factory method.
    /// (3) The Dispose method should call IPooledObject<T>.Return(T obj) to return the object to the pool.
    /// (4) The Dispose method should never be called more than once, or undefined behavior may occur.
    /// (5) SetDefault *must* reset the object state back to it's default state, or undefined behavior may occur.
    /// (6) Make sure you do not store references to the object after it has been returned to the pool, or undefined behavior may occur.
    /// </summary>
    /// <typeparam name="T">Class Type</typeparam>
    public interface IPooledObject<T> : IDisposable
        where T : class, IPooledObject<T>
    {
        /// <summary>
        /// Defines a method that will reset the object state back to it's default state, so it can be rented out again.
        /// ***Called Internally by the Object Pool on Disposal.***
        /// </summary>
        void SetDefault();

        #region Static Members
        /// <summary>
        /// Rent an object from the Object Pool.
        /// </summary>
        /// <returns>Pool object.</returns>
        static T Rent()
        {
            return ObjectPool.Rent();
        }

        /// <summary>
        /// Return an object back to the Object Pool.
        /// </summary>
        /// <param name="obj">Pool Object to return.</param>
        static void Return(T obj)
        {
            if (obj is IPooledObject<T> pooledObj)
            {
                pooledObj.SetDefault();
                ObjectPool.Return(obj); // Return the base type not the interface type
            }
            else
            {
                Log.WriteLine($"CRITICAL ERROR: Unable to return '{obj.GetType()}' object to the ObjectPool!");
            }
        }

        private static class ObjectPool
        {
            // ConcurrentStack has simpler atomic push/pop (no thread-local affinity overhead
            // that ConcurrentBag carries), making cross-thread rent/return cheaper.
            private static readonly ConcurrentStack<T> _objectPool = new();
            // Cached factory avoids Activator.CreateInstance reflection on every pool miss.
            private static readonly Func<T> _factory =
                System.Linq.Expressions.Expression.Lambda<Func<T>>(
                    System.Linq.Expressions.Expression.New(typeof(T))).Compile();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static T Rent()
            {
                if (_objectPool.TryPop(out var obj))
                {
                    return obj;
                }
                else
                {
                    return _factory();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static void Return(T obj)
            {
                _objectPool.Push(obj);
            }
        }
        #endregion
    }
}
