using MessagePack;
using MessagePack.Formatters;

namespace eft_dma_radar.Common.Misc.MessagePack
{
    public class Vector2Formatter : IMessagePackFormatter<System.Numerics.Vector2>
    {
        public System.Numerics.Vector2 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            ArgumentOutOfRangeException.ThrowIfNotEqual(reader.ReadArrayHeader(), 2, nameof(reader));
            System.Numerics.Vector2 result;
            result.X = reader.ReadSingle();
            result.Y = reader.ReadSingle();

            reader.Depth--;
            return result;
        }

        public void Serialize(ref MessagePackWriter writer, System.Numerics.Vector2 value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.Write(value.X);
            writer.Write(value.Y);
        }
    }
}
