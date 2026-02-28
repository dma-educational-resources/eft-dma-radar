using System.Runtime.InteropServices;
using Vmmsharp;

namespace eft_dma_radar.Common.DMA.ScatterAPI
{
    /// <summary>
    /// Wraps Memory Writing functionality via Vmmsharp Scatter API.
    /// No-op on zero entries.
    /// </summary>
    public sealed class ScatterWriteHandle : IDisposable
    {
        private readonly uint _pid;
        private readonly VmmScatterMemory _handle;
        private int _count = 0;
        /// <summary>
        /// Callbacks executed on completion.
        /// You MUST handle exceptions.
        /// </summary>
        public Action Callbacks { get; set; }

        public ScatterWriteHandle()
        {
            _handle = Memory.GetScatter(Vmm.FLAG_NOCACHE);
        }

        /// <summary>
        /// Adds a buffer of values <typeparamref name="T"/> to write.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="va">Virtual address.</param>
        /// <param name="buffer">Buffer to write.</param>
        /// <exception cref="Exception"></exception>
        public void AddBufferEntry<T>(ulong va, Span<T> buffer)
            where T : unmanaged
        {
            int sizeInBytes = buffer.Length * Marshal.SizeOf<T>();
            byte[] byteArray = new byte[sizeInBytes];

            Span<byte> byteSpan = MemoryMarshal.AsBytes(buffer);
            byteSpan.CopyTo(byteArray);

            if (!_handle.PrepareWrite(va, byteArray))
                throw new Exception("Failed to prepare write entry.");

            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Adds an entry of value <typeparamref name="T"/> to write.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="va">Virtual address.</param>
        /// <param name="value">Value to write.</param>
        /// <exception cref="Exception"></exception>
        public void AddValueEntry<T>(ulong va, T value)
            where T : unmanaged
        {
            if (!_handle.PrepareWriteStruct(va, value))
                throw new Exception("Failed to prepare write entry.");
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Adds an entry of byref value <typeparamref name="T"/> to write.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="va">Virtual address.</param>
        /// <param name="value">Value to write.</param>
        /// <exception cref="Exception"></exception>
        public void AddValueEntry<T>(ulong va, ref T value)
            where T : unmanaged
        {
            if (!_handle.PrepareWriteStruct(va, value))
                throw new Exception("Failed to prepare write entry.");
            Interlocked.Increment(ref _count);
        }

        /// <summary>
        /// Performs a Scatter Write operation with all prepared entries.
        /// </summary>
        /// <param name="validation">Validation before writing, checks for True.</param>
        public void Execute(Func<bool> validation)
        {
            if (!SharedProgram.Config.MemWritesEnabled)
                throw new Exception("Memory Writing is Disabled!");
            if (_count > 0)
            {
                if (!validation())
                    throw new Exception("Validation Callback Returned False (DO NOT WRITE).");
                if (!_handle.Execute())
                    throw new Exception("Memory Write Failed!");
                Callbacks?.Invoke();
            }
        }

        /// <summary>
        /// Clear the scatter write handle for fresh re-use.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void Clear()
        {
            _count = default;
            Callbacks = default;
            if (!_handle.Clear(Vmm.FLAG_NOCACHE))
                throw new Exception("Failed to clear handle!");
        }

        #region IDisposable
        private bool _disposed = false;
        public void Dispose()
        {
            bool disposed = Interlocked.Exchange(ref _disposed, true);
            if (!disposed)
            {
                _handle.Dispose();
            }
        }
        #endregion
    }
}
