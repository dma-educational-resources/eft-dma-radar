using MessagePack;
using MessagePack.Formatters;
using System.Numerics;

namespace eft_dma_radar.Common.Misc.MessagePack.ESPServerMessagePack
{
    public class Vector2Formatter : IMessagePackFormatter<Vector2>
    {
        public Vector2 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            options.Security.DepthStep(ref reader);

            if (reader.ReadArrayHeader() != 2)
                throw new MessagePackSerializationException("Vector2 must be an array of 2 elements.");

            float x = reader.ReadSingle();
            float y = reader.ReadSingle();

            reader.Depth--;
            return new Vector2(x, y);
        }

        public void Serialize(ref MessagePackWriter writer, Vector2 value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.Write(value.X);
            writer.Write(value.Y);
        }
    }
}
