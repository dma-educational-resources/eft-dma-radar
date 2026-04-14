using VmmSharpEx;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace eft_dma_radar.Silk.DMA.ScatterAPI
{
    /// <summary>
    /// Wraps scatter-write functionality via the VmmSharpEx Scatter API.
    /// No-op when zero entries are queued.
    /// </summary>
    public sealed class ScatterWriteHandle : IDisposable
    {
        private readonly VmmScatter _handle;
        private int _count;

        /// <summary>Callbacks executed after a successful <see cref="Execute"/> call. Must not throw.</summary>
        public Action? Callbacks { get; set; }

        public ScatterWriteHandle()
        {
            _handle = Memory.GetScatter(VmmFlags.NOCACHE);
        }

        /// <summary>Queues a value-type write.</summary>
        public void AddValueEntry<T>(ulong va, T value)
            where T : unmanaged
        {
            if (!_handle.PrepareWriteValue<T>(va, in value))
                throw new Exception("Failed to prepare scatter write entry.");
            Interlocked.Increment(ref _count);
        }

        /// <summary>Queues a by-ref value-type write.</summary>
        public void AddValueEntry<T>(ulong va, ref T value)
            where T : unmanaged
        {
            if (!_handle.PrepareWriteValue<T>(va, in value))
                throw new Exception("Failed to prepare scatter write entry.");
            Interlocked.Increment(ref _count);
        }

        /// <summary>Queues a buffer write.</summary>
        public void AddBufferEntry<T>(ulong va, Span<T> buffer)
            where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(buffer);
            if (!_handle.PrepareWriteSpan<byte>(va, bytes))
                throw new Exception("Failed to prepare scatter write buffer entry.");
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Executes all queued writes after passing a validation gate.
        /// Throws if writes are globally disabled or validation returns false.
        /// </summary>
        public void Execute(Func<bool> validation)
        {
            if (!SilkProgram.Config.MemWritesEnabled)
                throw new InvalidOperationException("Memory writes are disabled.");
            if (_count == 0)
                return;
            if (!validation())
                throw new InvalidOperationException("Write validation returned false.");
            _handle.Execute();
            Callbacks?.Invoke();
        }

        /// <summary>Clears all queued entries and callbacks for reuse.</summary>
        public void Clear()
        {
            _count = 0;
            Callbacks = null;
            _handle.Clear(VmmFlags.NOCACHE);
        }

        #region IDisposable
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _handle.Dispose();
        }
        #endregion
    }
}
