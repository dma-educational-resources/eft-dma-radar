using System.IO;
using System.Text;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;

namespace eft_dma_radar.Common.Unity.LowLevel.Types
{
    public sealed class RemoteBytes : IDisposable
    {
        public static implicit operator ulong(RemoteBytes x) => x._pmem;

        private readonly uint _size;
        private ulong _pmem;
        public readonly struct MonoString
        {
            private readonly byte[] _p1;
            private readonly byte[] Length;
            private readonly byte[] Data;
        
            private const int MonoString_p1 = 0x10;
        
            public MonoString(string data)
            {
                _p1 = new byte[MonoString_p1];
                Length = BitConverter.GetBytes(data.Length);
                Data = Encoding.Unicode.GetBytes(data);
            }
        
            public int GetSize() => MonoString_p1 + Length.Length + Data.Length;
            public uint GetSizeU() => (uint)GetSize();
        
            public byte[] GetBytes()
            {
                byte[] bytes = new byte[GetSize()];
                using MemoryStream memoryStream = new(bytes);
                using BinaryWriter writer = new(memoryStream);
                writer.Write(_p1);
                writer.Write(Length);
                writer.Write(Data);
                return bytes;
            }
        
            public static MonoString Get(string str) => new(str);
        }
        public void WriteString(MonoString monoString)
        {
            int byteSize = monoString.GetSize();
            if (byteSize > _size)
                throw new Exception($"String size {byteSize} is larger than allocated memory size {_size} bytes!");
        
            Memory.WriteBufferEnsure<byte>(_pmem, monoString.GetBytes());
        }

        public RemoteBytes(int size)
        {
            //_size = MemDMABase.AlignLength((uint)size);
            //_pmem = NativeMethods.AllocBytes(_size);
            //_pmem.ThrowIfInvalidVirtualAddress();
        }

        public RemoteBytes(IMonoType data)
        {
            //_size = MemDMABase.AlignLength((uint)data.Data.Length);
            //_pmem = NativeMethods.AllocBytes(_size);
            //_pmem.ThrowIfInvalidVirtualAddress();
            //WriteMonoValue(data);
        }

        public void WriteValue<T>(T value)
            where T : unmanaged
        {
            int writeSize = SizeChecker<T>.Size;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(writeSize, (int)_size, nameof(writeSize));

            Memory.WriteValueEnsure(_pmem, value);
        }

        public void WriteMonoValue(IMonoType value)
        {
            int writeSize = value.Data.Length;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(writeSize, (int)_size, nameof(writeSize));

            Memory.WriteBufferEnsure(_pmem, value.Data);
        }

        public void WriteBuffer<T>(Span<T> buffer)
            where T : unmanaged
        {
            int writeSize = SizeChecker<T>.Size * buffer.Length;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(writeSize, (int)_size, nameof(writeSize));

            Memory.WriteBufferEnsure(_pmem, buffer);
        }

        public void Dispose()
        {
            ulong pmem = Interlocked.Exchange(ref _pmem, 0);
            if (pmem != 0x0)
            {
                //NativeMethods.FreeBytes(pmem);
            }
        }
    }
}
