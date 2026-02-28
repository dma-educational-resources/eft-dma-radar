using System.Buffers.Binary;
using System.Text;

namespace eft_dma_radar.Common.Unity.LowLevel.Types
{
    public sealed class MonoString : IMonoType
    {
        private const int _p1 = 0x10;
        private readonly byte[] _data;

        /// <summary>
        /// Raw Data for this object. Includes inserted padding and length.
        /// </summary>
        public Span<byte> Data => _data;

        /// <summary>
        /// Create a MonoString from a string.
        /// </summary>
        /// <param name="data"></param>
        public MonoString(string data)
        {
            var dataBytes = Encoding.Unicode.GetBytes(data);
            _data = new byte[_p1 + 4 + dataBytes.Length];
            BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(_p1), data.Length);
            dataBytes.CopyTo(_data, _p1 + 4);
        }

        public RemoteBytes ToRemoteBytes()
        {
            return new RemoteBytes(this);
        }
    }
}
