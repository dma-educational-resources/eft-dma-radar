using System;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eft_dma_radar.Common.Misc.MessagePack
{
    public class Vector2JsonConverter : JsonConverter<Vector2>
    {
        public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Invalid JSON format for Vector2. Expected an array.");

            reader.Read();
            float x = reader.GetSingle();
            reader.Read();
            float y = reader.GetSingle();

            if (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Invalid JSON format for Vector2. Expected end of array.");

            return new Vector2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }
}
