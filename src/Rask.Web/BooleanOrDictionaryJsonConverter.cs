using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Web;

// A field MDN types as a boolean or a dictionary (a media request's video), which C# holds as the dictionary, null for
// `false`. It is written as the dictionary, an empty one meaning `true`; read back, the browser's `true` is an empty one
// and its `false` is null, so a page that set the boolean still reads.
internal sealed class BooleanOrDictionaryJsonConverter<T> : JsonConverter<T>
    where T : class, new()
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => new T(),
            JsonTokenType.False => null,
            _ => JsonSerializer.Deserialize(ref reader, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))),
        };

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
}
