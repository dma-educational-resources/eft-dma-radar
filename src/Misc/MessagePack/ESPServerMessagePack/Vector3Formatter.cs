using MessagePack;
using MessagePack.Formatters;
using System.Numerics;

namespace eft_dma_radar.Common.Misc.MessagePack.ESPServerMessagePack
{
    public class Vector3Formatter : IMessagePackFormatter<Vector3>
    {
        public Vector3 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            if (reader.ReadArrayHeader() != 3)
                throw new MessagePackSerializationException("Vector3 must be an array of 3 elements.");

            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();

            reader.Depth--;
            return new Vector3(x, y, z);
        }

        public void Serialize(ref MessagePackWriter writer, Vector3 value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }
    }
}
