using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Web;

// Bytes to the browser and back. A plain base64 string would arrive as a string, which a web API takes for text, so on
// the way out the bytes are marked — {"__raskBytes__": "AQID"} — for the browser to turn back into a Uint8Array. On the
// way back the browser sends a buffer or a view as plain base64.
internal sealed class BytesJsonConverter : JsonConverter<byte[]>
{
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : reader.GetBytesFromBase64();

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBase64String("__raskBytes__", value);
        writer.WriteEndObject();
    }
}
