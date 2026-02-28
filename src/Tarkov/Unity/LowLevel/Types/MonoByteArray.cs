using System.Buffers.Binary;

namespace eft_dma_radar.Common.Unity.LowLevel.Types
{
    public sealed class MonoByteArray : IMonoType
    {
        private const int _p1 = 0x18;
        private const int _p2 = 0x4;
        private readonly byte[] _data;

        /// <summary>
        /// Raw Data for this object. Includes inserted padding and length.
        /// </summary>
        public Span<byte> Data => _data;

        public MonoByteArray(byte[] data)
        {
            _data = new byte[_p1 + 4 + _p2 + data.Length];
            BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(_p1), data.Length);
            data.CopyTo(_data, _p1 + 4 + _p2);
        }

        public RemoteBytes ToRemoteBytes()
        {
            return new RemoteBytes(this);
        }
    }
}
