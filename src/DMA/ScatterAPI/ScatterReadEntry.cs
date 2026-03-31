using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Pools;
using System.Runtime.CompilerServices;
using System.Text;
using VmmSharpEx.Scatter;
using static eft_dma_radar.Common.Unity.UnityTransform;

namespace eft_dma_radar.Common.DMA.ScatterAPI
{
    public sealed class ScatterReadEntry<T> : IScatterEntry, IPooledObject<ScatterReadEntry<T>>
    {
        private static readonly bool _isValueType = !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        private T _result;
        /// <summary>
        /// Result for this read. Should only be called from the Index.
        /// Be sure to check the IsFailed flag.
        /// </summary>
        internal ref T Result => ref _result;
        /// <summary>
        /// Virtual Address to read from.
        /// </summary>
        public ulong Address { get; private set; }
        /// <summary>
        /// Count of bytes to read.
        /// </summary>
        public int CB { get; private set; }
        /// <summary>
        /// True if this read has failed, otherwise False.
        /// </summary>
        public bool IsFailed { get; set; }
        public Action<ScatterReadEntry<T>> ActionOnComplete { get; set; }
        [Obsolete("You must rent this object via IPooledObject!")]
        public ScatterReadEntry() { }

        /// <summary>
        /// Get a Scatter Read Entry from the Object Pool.
        /// </summary>
        /// <param name="address">Virtual Address to read from.</param>
        /// <param name="cb">Count of bytes to read.</param>
        /// <returns>Rented ScatterReadEntry <typeparamref name="T"/> instance.</returns>
        public static ScatterReadEntry<T> Get(ulong address, int cb)
        {
            var entry = IPooledObject<ScatterReadEntry<T>>.Rent();
            entry.Configure(address, cb);
            return entry;
        }

        /// <summary>
        /// Configure this entry.
        /// </summary>
        /// <param name="address">Virtual Address to read from.</param>
        /// <param name="cb">Count of bytes to read.</param>
        private void Configure(ulong address, int cb)
        {
            Address = address;
            if (cb == 0 && _isValueType)
                cb = SizeChecker<T>.Size;
            CB = cb;
        }

        /// <summary>
        /// Extracts the result for this entry from the executed native scatter handle.
        /// Only called internally via API.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadResult(VmmScatter scatter)
        {
            try
            {
                if (_isValueType)
                    SetValueResult(scatter);
                else
                    SetClassResult(scatter);
            }
            catch
            {
                IsFailed = true;
            }

            // 🔥 VERY IMPORTANT — notify IL2CPP loader
            ActionOnComplete?.Invoke(this);
        }

        /// <summary>
        /// Read a value type result directly from the native scatter handle.
        /// </summary>
        private unsafe void SetValueResult(VmmScatter scatter)
        {
            int cb = SizeChecker<T>.Size;
#pragma warning disable CS8500
            fixed (void* pb = &_result)
            {
                var buffer = new Span<byte>(pb, cb);
                if (!scatter.ReadSpan<byte>(Address, buffer))
                {
                    IsFailed = true;
                    return;
                }
            }
#pragma warning restore CS8500
            if (_result is MemPointer memPtrResult && !Utils.IsValidVirtualAddress(memPtrResult))
                IsFailed = true;
        }

        /// <summary>
        /// Read a class/reference type result directly from the native scatter handle.
        /// </summary>
        private void SetClassResult(VmmScatter scatter)
        {
            if (this is ScatterReadEntry<SharedArray<TrsX>> r1) // vertices
            {
                int size = SizeChecker<TrsX>.Size;
                ArgumentOutOfRangeException.ThrowIfNotEqual(CB % size, 0, nameof(CB));
                int count = CB / size;
                var vert = SharedArray<TrsX>.Get(count);
                if (!scatter.ReadSpan(Address, vert.Span))
                {
                    vert.Dispose();
                    IsFailed = true;
                }
                else
                    r1._result = vert;
            }
            else if (this is ScatterReadEntry<SharedArray<MemPointer>> r2) // Pointers
            {
                int size = SizeChecker<MemPointer>.Size;
                ArgumentOutOfRangeException.ThrowIfNotEqual(CB % size, 0, nameof(CB));
                int count = CB / size;
                var ctr = SharedArray<MemPointer>.Get(count);
                if (!scatter.ReadSpan(Address, ctr.Span))
                {
                    ctr.Dispose();
                    IsFailed = true;
                }
                else
                    r2._result = ctr;
            }
            else if (this is ScatterReadEntry<UnicodeString> r3) // UTF-16
            {
                Span<byte> buffer = CB > 0x1000 ? new byte[CB] : stackalloc byte[CB];
                buffer.Clear();
                if (!scatter.ReadSpan(Address, buffer))
                {
                    IsFailed = true;
                    return;
                }
                var nullIndex = buffer.FindUtf16NullTerminatorIndex();
                r3._result = nullIndex >= 0 ?
                    Encoding.Unicode.GetString(buffer.Slice(0, nullIndex)) : Encoding.Unicode.GetString(buffer);
            }
            else if (this is ScatterReadEntry<UTF8String> r4) // UTF-8
            {
                Span<byte> buffer = CB > 0x1000 ? new byte[CB] : stackalloc byte[CB];
                buffer.Clear();
                if (!scatter.ReadSpan(Address, buffer))
                {
                    IsFailed = true;
                    return;
                }
                var nullIndex = buffer.IndexOf((byte)0);
                r4._result = nullIndex >= 0 ?
                    Encoding.UTF8.GetString(buffer.Slice(0, nullIndex)) : Encoding.UTF8.GetString(buffer);
            }
            else
                throw new NotImplementedException($"Type {typeof(T)} not supported!");
        }

        public void Dispose()
        {
            IPooledObject<ScatterReadEntry<T>>.Return(this);
        }

        public void SetDefault()
        {
            if (_result is IDisposable disposable)
                disposable.Dispose();
            _result = default;
            Address = default;
            CB = default;
            IsFailed = default;

            ActionOnComplete = null;
        }
    }
}
