using MessagePack;
using MessagePack.Formatters;

namespace eft_dma_radar.Common.Misc.MessagePack
{
    public class Vector3Formatter : IMessagePackFormatter<System.Numerics.Vector3>
    {
        public System.Numerics.Vector3 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            ArgumentOutOfRangeException.ThrowIfNotEqual(reader.ReadArrayHeader(), 3, nameof(reader));
            System.Numerics.Vector3 result;
            result.X = reader.ReadSingle();
            result.Y = reader.ReadSingle();
            result.Z = reader.ReadSingle();

            reader.Depth--;
            return result;
        }

        public void Serialize(ref MessagePackWriter writer, System.Numerics.Vector3 value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }
    }
}
