using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Logging;

/// <summary>
///     Encodes captured scope state for the <c>Scopes</c> column and reads it back.
/// </summary>
/// <remarks>
///     <para>
///         A JSON object rather than a side table. A side table would be the textbook answer, but it buys
///         normalisation nobody here needs and costs an insert per pair on the highest-frequency writer in
///         the framework — plus a join on every read of a log that is already read far less often than it
///         is written. The store is append-only and expendable; the column is the right shape for it.
///     </para>
///     <para>
///         Serialized with an explicit context so the package stays trim- and AOT-safe: the reflection
///         serializer would be an IL2026/IL3050 site in a published WASM or AOT app.
///     </para>
/// </remarks>
internal static class LogScopeJson
{
    internal static string? Encode(IReadOnlyList<LogScopeValue>? scopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            return null;
        }

        // A flat object: duplicate keys across nested scopes are rare, and last-wins matches how a reader
        // would interpret "the innermost value of RequestId" anyway.
        var map = new Dictionary<string, string>(scopes.Count, StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            map[scope.Key] = scope.Value;
        }

        return JsonSerializer.Serialize(map, LogScopeJsonContext.Default.DictionaryStringString);
    }

    internal static IReadOnlyList<LogScopeValue>? Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var map = JsonSerializer.Deserialize(json, LogScopeJsonContext.Default.DictionaryStringString);
            if (map is null || map.Count == 0)
            {
                return null;
            }

            var values = new List<LogScopeValue>(map.Count);
            foreach (var pair in map)
            {
                values.Add(new LogScopeValue(pair.Key, pair.Value));
            }

            return values;
        }
        catch (JsonException)
        {
            // A row written by something else, or a truncated write. A log viewer must not throw on one
            // malformed row and take the whole page with it.
            return null;
        }
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LogScopeJsonContext : JsonSerializerContext;
