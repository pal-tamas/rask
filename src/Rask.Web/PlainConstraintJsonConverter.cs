using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Web;

// A media constraint (MDN's ConstrainDouble, ConstrainDOMString…), which C# holds as its plain value: `Width = 640`,
// which the browser takes as the ideal. It is written as the value; read back, a constraint the page wrote as an object
// is its `ideal`, else its `exact`, a list is its first item, and anything else (a range alone) is null.
internal sealed class PlainConstraintJsonConverter<T> : JsonConverter<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Plain(document.RootElement, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, TypeInfo(options));

    private static T? Plain(JsonElement value, JsonSerializerOptions options)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                return value.TryGetProperty("ideal", out var ideal) || value.TryGetProperty("exact", out ideal) ? Plain(ideal, options) : default;
            case JsonValueKind.Array:
                return value.GetArrayLength() > 0 ? Plain(value[0], options) : default;
            default:
                try
                {
                    return value.Deserialize(TypeInfo(options));
                }
                catch (JsonException)
                {
                    // A value of another kind than declared (text where a number is) is no reading of this one.
                    return default;
                }
        }
    }

    private static JsonTypeInfo<T> TypeInfo(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
