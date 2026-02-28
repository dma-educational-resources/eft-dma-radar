using System.Runtime.CompilerServices;

namespace eft_dma_radar.Common.Misc
{
    /// <summary>
    /// Caches Type Sizes of value types.
    /// </summary>
    /// <typeparam name="T">Type to check.</typeparam>
    public static class SizeChecker<T>
    {
        /// <summary>
        /// Size of this Type.
        /// </summary>
        public static readonly int Size = GetSize();

        private static int GetSize()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new NotSupportedException(typeof(T).ToString());
            return Unsafe.SizeOf<T>();
        }
    }
}
