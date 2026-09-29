using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Core;

/// <summary>JSON shape for <see cref="ElementRef" />: <c>{"__raskRef__":"id"}</c>, matched by the client reviver.</summary>
// A typed ElementRef<T> takes it too (declared on it as well: the attribute is read off the runtime type only), and
// writes the same marker, so the client revives either to the element.
internal sealed class ElementRefJsonConverter : JsonConverter<ElementRef>
{
    public override bool CanConvert(Type typeToConvert) => typeof(ElementRef).IsAssignableFrom(typeToConvert);

    public override ElementRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? id = null;
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && string.Equals(reader.GetString(), ElementRef.Marker, StringComparison.Ordinal))
                {
                    reader.Read();
                    id = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new ElementRef(id ?? string.Empty);
    }

    public override void Write(Utf8JsonWriter writer, ElementRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(ElementRef.Marker, value.Id ?? string.Empty);
        writer.WriteEndObject();
    }
}
