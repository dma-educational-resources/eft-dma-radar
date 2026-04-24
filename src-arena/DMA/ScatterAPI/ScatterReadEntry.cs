using VmmSharpEx.Scatter;
using ArenaUtils = eft_dma_radar.Arena.Misc.Utils;

namespace eft_dma_radar.Arena.DMA.ScatterAPI
{
    [method: Obsolete("Rent via IPooledObject<ScatterReadEntry<T>>.Rent().")]
    public sealed class ScatterReadEntry<T>() : IScatterEntry, IPooledObject<ScatterReadEntry<T>>
    {
        private static readonly bool _isValueType = !RuntimeHelpers.IsReferenceOrContainsReferences<T>();
        private T _result = default!;

        internal ref T Result => ref _result;

        public ulong Address { get; private set; }
        public int CB { get; private set; }
        public bool IsFailed { get; set; }
        public Action<ScatterReadEntry<T>>? ActionOnComplete { get; set; }

        public static ScatterReadEntry<T> Get(ulong address, int cb)
        {
            var e = IPooledObject<ScatterReadEntry<T>>.Rent();
            e.Configure(address, cb);
            return e;
        }

        private void Configure(ulong address, int cb)
        {
            Address = address;
            if (cb == 0 && _isValueType)
                cb = SizeChecker<T>.Size;
            CB = cb;
        }

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
            ActionOnComplete?.Invoke(this);
        }

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
            if (_result is MemPointer mp && !ArenaUtils.IsValidVirtualAddress(mp))
                IsFailed = true;
        }

        private void SetClassResult(VmmScatter scatter)
        {
            if (this is ScatterReadEntry<SharedArray<MemPointer>> r2)
            {
                int size = SizeChecker<MemPointer>.Size;
                ArgumentOutOfRangeException.ThrowIfNotEqual(CB % size, 0, nameof(CB));
                int count = CB / size;
                var arr = SharedArray<MemPointer>.Get(count);
                if (!scatter.ReadSpan(Address, arr.Span))
                {
                    arr.Dispose();
                    IsFailed = true;
                }
                else
                    r2._result = arr;
            }
            else if (this is ScatterReadEntry<eft_dma_radar.Arena.Misc.UnicodeString> r3)
            {
                Span<byte> buf = CB > 0x1000 ? new byte[CB] : stackalloc byte[CB];
                buf.Clear();
                if (!scatter.ReadSpan(Address, buf)) { IsFailed = true; return; }
                var ro = (ReadOnlySpan<byte>)buf;
                var nullIdx = eft_dma_radar.Arena.Misc.Extensions.FindUtf16NullTerminatorIndex(ro);
                r3._result = nullIdx >= 0
                    ? Encoding.Unicode.GetString(buf[..nullIdx])
                    : Encoding.Unicode.GetString(buf);
            }
            else if (this is ScatterReadEntry<eft_dma_radar.Arena.Misc.UTF8String> r4)
            {
                Span<byte> buf = CB > 0x1000 ? new byte[CB] : stackalloc byte[CB];
                buf.Clear();
                if (!scatter.ReadSpan(Address, buf)) { IsFailed = true; return; }
                var nullIdx = buf.IndexOf((byte)0);
                r4._result = nullIdx >= 0
                    ? Encoding.UTF8.GetString(buf[..nullIdx])
                    : Encoding.UTF8.GetString(buf);
            }
            else
                throw new NotImplementedException($"Type {typeof(T)} not supported in scatter read.");
        }

        public void Dispose() => IPooledObject<ScatterReadEntry<T>>.Return(this);

        public void SetDefault()
        {
            if (_result is IDisposable d) d.Dispose();
            _result = default!;
            Address = default;
            CB = default;
            IsFailed = default;
            ActionOnComplete = null;
        }
    }
}
