using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MessagePack;
using MessagePack.Formatters;
namespace eft_dma_radar.Common.Misc.MessagePack.ESPServerMessagePack
{
    public class Vector3DictionaryFormatter : IMessagePackFormatter<Dictionary<string, Vector3>>
    {
        public void Serialize(ref MessagePackWriter writer, Dictionary<string, Vector3> value, MessagePackSerializerOptions options)
        {
            writer.WriteMapHeader(value.Count);
            foreach (var kvp in value)
            {
                writer.Write(kvp.Key);
                writer.WriteArrayHeader(3);
                writer.Write(kvp.Value.X);
                writer.Write(kvp.Value.Y);
                writer.Write(kvp.Value.Z);
            }
        }
    
        public Dictionary<string, Vector3> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadMapHeader();
            var dictionary = new Dictionary<string, Vector3>(count);
    
            for (int i = 0; i < count; i++)
            {
                string key = reader.ReadString();
                reader.ReadArrayHeader(); // Expecting 3 elements
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                dictionary[key] = new Vector3(x, y, z);
            }
    
            return dictionary;
        }
    }
}