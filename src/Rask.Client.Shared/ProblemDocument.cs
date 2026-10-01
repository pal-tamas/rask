using System.Text.Json;

namespace Rask.Client.Shared;

/// <summary>
/// The members of an RFC 9457 problem document a Rask client reports: type, title, detail and — for a rejected
/// request — the field errors, in the shape ASP.NET's own <c>ValidationProblemDetails</c> uses too.
/// </summary>
/// <remarks>
/// Hand-read rather than deserialized: the client packages do no reflection anywhere. Source-linked into
/// Rask.Api.Client and Rask.Cqrs.Client, which carried a copy each until one learned to step over a nested value
/// and the other did not.
/// </remarks>
internal sealed record ProblemDocument(
    string? Type,
    string? Title,
    string? Detail,
    Dictionary<string, string[]>? Errors)
{
    /// <summary>Reads <paramref name="json"/>; a body that is not a JSON object yields an empty document.</summary>
    /// <exception cref="JsonException">The body is not valid JSON.</exception>
    public static ProblemDocument Read(ReadOnlySpan<byte> json)
    {
        string? type = null;
        string? title = null;
        string? detail = null;
        Dictionary<string, string[]>? errors = null;

        var reader = new Utf8JsonReader(json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return new ProblemDocument(null, null, null, null);
        }

        // Skip() rather than hand-tracked depth: a nested object or array consumed as a property VALUE without
        // stepping over it leaves its closing token to read as top-level, and parsing stops there — or reads a
        // property name off a value token and throws.
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                // `errors` is named explicitly: everything unknown is skipped, so field errors would otherwise be
                // dropped and the caller would see a 400 with nothing to show the user.
                if (name is "errors" && reader.TokenType == JsonTokenType.StartObject)
                {
                    errors = ReadErrors(ref reader);
                    continue;
                }

                reader.Skip();
                continue;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                continue;
            }

            switch (name)
            {
                case "type":
                    type = reader.GetString();
                    break;
                case "title":
                    title = reader.GetString();
                    break;
                case "detail":
                    detail = reader.GetString();
                    break;
            }
        }

        return new ProblemDocument(type, title, detail, errors);
    }

    private static Dictionary<string, string[]>? ReadErrors(ref Utf8JsonReader reader)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var field = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                continue;
            }

            var messages = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } message)
                {
                    messages.Add(message);
                    continue;
                }

                reader.Skip();
            }

            result[field] = [.. messages];
        }

        return result.Count == 0 ? null : result;
    }
}
