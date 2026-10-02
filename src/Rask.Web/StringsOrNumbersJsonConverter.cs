using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Web;

// A list of what StringOrNumberJsonConverter carries (a Bluetooth filter's services), each item crossing as it would.
internal sealed class StringsOrNumbersJsonConverter : JsonConverter<string[]>
{
    private static readonly StringOrNumberJsonConverter Item = new();

    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var items = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(Item.Read(ref reader, typeof(string), options)!);
        }

        return [.. items];
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            Item.Write(writer, item, options);
        }

        writer.WriteEndArray();
    }
}
