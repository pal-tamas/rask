using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Web;

// A field MDN types as a value or a list of it (an ICE server's urls), which C# holds as the list. It is written as the
// list; read back, a lone value (what a page set as one, and the browser hands back as it was) is a list of one.
internal sealed class OneOrManyJsonConverter<T> : JsonConverter<T[]>
{
    public override T[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.StartArray
            ? JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<T[]>())
            : [JsonSerializer.Deserialize(ref reader, options.GetTypeInfo<T>())!];

    public override void Write(Utf8JsonWriter writer, T[] value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options.GetTypeInfo<T[]>());
}
