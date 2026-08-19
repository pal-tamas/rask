using System.Globalization;
using System.Text.Json;

namespace Rask.Cqrs;

/// <summary>
///     The primitives generated wire codecs are built from. Public only so generated code can call
///     them; you do not use them directly.
/// </summary>
/// <remarks>
///     <para>
///         These live in the runtime package rather than being emitted into every assembly for two
///         reasons: the generated file stays small enough to read, and every "the wire said X where Y
///         was expected" message is written once, in one place, with the property name in it. A codec
///         failure is otherwise one of the least legible errors a framework can produce.
///     </para>
///     <para>
///         Every reader takes the property name purely to name it in the exception. The cost is a
///         string literal already in the metadata; the benefit is an error that says which field of
///         which message disagreed.
///     </para>
/// </remarks>
public static class WireJson
{
    /// <summary>Reads a <see cref="bool" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static bool ReadBoolean(ref Utf8JsonReader reader, string property) => reader.TokenType switch
    {
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        _ => throw Unexpected(ref reader, property, "a boolean"),
    };

    /// <summary>Reads a <see cref="string" />, allowing JSON null.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static string? ReadString(ref Utf8JsonReader reader, string property) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Null => null,
        _ => throw Unexpected(ref reader, property, "a string"),
    };

    /// <summary>Reads a <see cref="string" /> that the message declares as non-nullable.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static string ReadRequiredString(ref Utf8JsonReader reader, string property) =>
        ReadString(ref reader, property)
        ?? throw new JsonException(
            $"'{property}' arrived as null, but the message declares it as a non-nullable string. The sender "
            + "and the receiver are compiled against different versions of the contract.");

    /// <summary>Reads a <see cref="char" />, which travels as a one-character string.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static char ReadChar(ref Utf8JsonReader reader, string property)
    {
        var text = ReadRequiredString(ref reader, property);
        return text.Length == 1
            ? text[0]
            : throw new JsonException(
                $"'{property}' is a char, so it must arrive as a one-character string; got {text.Length} characters.");
    }

    /// <summary>Reads a <see cref="byte" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static byte ReadByte(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out byte v) => r.TryGetByte(out v), "a byte");

    /// <summary>Reads an <see cref="sbyte" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static sbyte ReadSByte(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out sbyte v) => r.TryGetSByte(out v), "an sbyte");

    /// <summary>Reads a <see cref="short" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static short ReadInt16(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out short v) => r.TryGetInt16(out v), "a short");

    /// <summary>Reads a <see cref="ushort" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static ushort ReadUInt16(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out ushort v) => r.TryGetUInt16(out v), "a ushort");

    /// <summary>Reads an <see cref="int" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static int ReadInt32(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out int v) => r.TryGetInt32(out v), "an int");

    /// <summary>Reads a <see cref="uint" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static uint ReadUInt32(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out uint v) => r.TryGetUInt32(out v), "a uint");

    /// <summary>Reads a <see cref="long" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static long ReadInt64(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out long v) => r.TryGetInt64(out v), "a long");

    /// <summary>Reads a <see cref="ulong" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static ulong ReadUInt64(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out ulong v) => r.TryGetUInt64(out v), "a ulong");

    /// <summary>Reads a <see cref="float" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static float ReadSingle(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out float v) => r.TryGetSingle(out v), "a float");

    /// <summary>Reads a <see cref="double" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static double ReadDouble(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out double v) => r.TryGetDouble(out v), "a double");

    /// <summary>Reads a <see cref="decimal" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static decimal ReadDecimal(ref Utf8JsonReader reader, string property) =>
        Number(ref reader, property, static (ref Utf8JsonReader r, out decimal v) => r.TryGetDecimal(out v), "a decimal");

    /// <summary>Reads a <see cref="Guid" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static Guid ReadGuid(ref Utf8JsonReader reader, string property) =>
        reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var value)
            ? value
            : throw Unexpected(ref reader, property, "a GUID string");

    /// <summary>Reads a <see cref="DateTime" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static DateTime ReadDateTime(ref Utf8JsonReader reader, string property) =>
        reader.TokenType == JsonTokenType.String && reader.TryGetDateTime(out var value)
            ? value
            : throw Unexpected(ref reader, property, "an ISO-8601 date-time string");

    /// <summary>Reads a <see cref="DateTimeOffset" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static DateTimeOffset ReadDateTimeOffset(ref Utf8JsonReader reader, string property) =>
        reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out var value)
            ? value
            : throw Unexpected(ref reader, property, "an ISO-8601 date-time string with an offset");

    /// <summary>Reads a <see cref="DateOnly" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static DateOnly ReadDateOnly(ref Utf8JsonReader reader, string property) =>
        DateOnly.TryParse(ReadRequiredString(ref reader, property), CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException($"'{property}' is not a yyyy-MM-dd date.");

    /// <summary>Reads a <see cref="TimeOnly" />.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static TimeOnly ReadTimeOnly(ref Utf8JsonReader reader, string property) =>
        TimeOnly.TryParse(ReadRequiredString(ref reader, property), CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException($"'{property}' is not an HH:mm:ss time.");

    /// <summary>Reads a <see cref="TimeSpan" />, which travels in the round-trip "c" format.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static TimeSpan ReadTimeSpan(ref Utf8JsonReader reader, string property) =>
        TimeSpan.TryParseExact(ReadRequiredString(ref reader, property), "c", CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException($"'{property}' is not a [-][d.]hh:mm:ss[.fffffff] duration.");

    /// <summary>Reads a <see cref="Uri" />, allowing JSON null.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static Uri? ReadUri(ref Utf8JsonReader reader, string property)
    {
        var text = ReadString(ref reader, property);
        if (text is null)
        {
            return null;
        }

        return Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out var value)
            ? value
            : throw new JsonException($"'{property}' is not a valid URI.");
    }

    /// <summary>Reads a byte array, which travels as base64, allowing JSON null.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static byte[]? ReadBytes(ref Utf8JsonReader reader, string property) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.String when reader.TryGetBytesFromBase64(out var value) => value,
        _ => throw Unexpected(ref reader, property, "a base64 string"),
    };

    /// <summary>
    ///     Resolves the file that was sent alongside the JSON at <paramref name="index" />.
    /// </summary>
    /// <param name="files">The files that arrived with the message, in index order.</param>
    /// <param name="index">The index written in the file's place, or -1 for none.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static RemoteFile? ResolveFile(IReadOnlyList<RemoteFile> files, int index, string property)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (index < 0)
        {
            return null;
        }

        return index < files.Count
            ? files[index]
            : throw new JsonException(
                $"'{property}' refers to file #{index}, but only {files.Count} arrived. The multipart body is "
                + "incomplete — it was truncated in transit, or a proxy dropped a part.");
    }

    /// <summary>Writes a <see cref="char" /> as a one-character string.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void WriteCharValue(Utf8JsonWriter writer, char value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }

    /// <summary>Writes a <see cref="TimeSpan" /> in the round-trip "c" format.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void WriteTimeSpanValue(Utf8JsonWriter writer, TimeSpan value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString("c", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a <see cref="DateOnly" /> as yyyy-MM-dd.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void WriteDateOnlyValue(Utf8JsonWriter writer, DateOnly value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a <see cref="TimeOnly" /> as HH:mm:ss.fffffff.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void WriteTimeOnlyValue(Utf8JsonWriter writer, TimeOnly value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
    }

    /// <summary>Writes a <see cref="Uri" /> as its original string, or JSON null.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value.</param>
    public static void WriteUriValue(Utf8JsonWriter writer, Uri? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.OriginalString);
    }

    /// <summary>
    ///     Advances past the value the reader is positioned on — how a codec ignores a property the
    ///     receiving side does not know, so adding a property to a message stays backwards compatible.
    /// </summary>
    /// <param name="reader">The reader, positioned on the value to discard.</param>
    public static void SkipValue(ref Utf8JsonReader reader) => reader.Skip();

    /// <summary>Throws unless the reader is positioned at the start of an object.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static void ExpectStartObject(ref Utf8JsonReader reader, string property)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw Unexpected(ref reader, property, "an object");
        }
    }

    /// <summary>Throws unless the reader is positioned at the start of an array.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="property">The property being read, for the error message.</param>
    public static void ExpectStartArray(ref Utf8JsonReader reader, string property)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw Unexpected(ref reader, property, "an array");
        }
    }

    private delegate bool TryRead<T>(ref Utf8JsonReader reader, out T value);

    private static T Number<T>(ref Utf8JsonReader reader, string property, TryRead<T> tryRead, string expected)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw Unexpected(ref reader, property, expected);
        }

        // A number that parses as JSON but not into the declared type is a range problem, not a shape one
        // — worth saying so, because "expected a number, got a number" would be a maddening error.
        return tryRead(ref reader, out var value)
            ? value
            : throw new JsonException($"'{property}' is outside the range of {expected}.");
    }

    private static JsonException Unexpected(ref Utf8JsonReader reader, string property, string expected) =>
        new($"'{property}' expected {expected} but the wire had {reader.TokenType}.");
}
