using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Web;

// A field MDN types as a string or a number (a Bluetooth service: "battery_service" or 0x180F; an animation's duration:
// "auto" or 500), which C# holds as a string. Text that reads as a number crosses as the number, since where the IDL also
// takes text that is a name ("auto", a mark's), the number written as text would be taken for a name. Read back, a
// number is its text.
internal sealed class StringOrNumberJsonConverter : JsonConverter<string>
{
    private const NumberStyles Number = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number ? reader.GetDouble().ToString(CultureInfo.InvariantCulture) : reader.GetString();

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (double.TryParse(value, Number, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
