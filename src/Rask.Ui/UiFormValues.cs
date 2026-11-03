using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Rask;

/// <summary>
///     The answers a control the browser holds carries in its one bound field: their form values, as a JSON array of
///     strings — <c>[]</c> for none, so an option whose value is the empty string is still one answer.
/// </summary>
/// <remarks>Written and read by hand, with no serializer to trim: <c>src/Rask.Site</c> publishes with no IL warnings.</remarks>
internal static class UiFormValues
{
    /// <summary>The field's value for <paramref name="values" />.</summary>
    /// <param name="values">The answers' form values, in order.</param>
    internal static string Write(IEnumerable<string> values)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The form values the field says, or none when it says something that is not such an array.</summary>
    /// <param name="field">What the browser sent.</param>
    internal static IReadOnlyList<string> Read(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(field);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. document.RootElement.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)];
        }
        catch (JsonException)
        {
            // Anything the browser can send arrives here: a field that does not parse holds no answer.
            return [];
        }
    }
}
