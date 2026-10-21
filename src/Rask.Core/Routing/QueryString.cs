using System.Text;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

public static class QueryString
{
    public static QueryCollection Parse(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            return QueryCollection.Empty;
        }

        var span = queryString.AsSpan();
        if (span[0] == '?')
        {
            span = span[1..];
        }

        if (span.Length == 0)
        {
            return QueryCollection.Empty;
        }

        var dict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        var start = 0;
        for (var i = 0; i <= span.Length; i++)
        {
            if (i != span.Length && span[i] != '&')
            {
                continue;
            }

            if (i == start)
            {
                start = i + 1;
                continue;
            }

            AddPair(dict, span[start..i]);
            start = i + 1;
        }

        return new QueryCollection(dict);
    }

    // One `key=value` (or bare `key`) pair, decoded; a repeated key accumulates its values.
    private static void AddPair(Dictionary<string, StringValues> dict, ReadOnlySpan<char> pair)
    {
        var eq = pair.IndexOf('=');
        string key;
        string value;
        if (eq < 0)
        {
            key = Decode(pair);
            value = string.Empty;
        }
        else
        {
            key = Decode(pair[..eq]);
            value = Decode(pair[(eq + 1)..]);
        }

        dict[key] = dict.TryGetValue(key, out var existing) ? StringValues.Concat(existing, value) : value;
    }

    private static string Decode(ReadOnlySpan<char> part) => Uri.UnescapeDataString(part.ToString().Replace('+', ' '));

    public static string Build(string path, IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);

        var sb = new StringBuilder(path);
        var first = true;
        foreach (var kv in query)
        {
            foreach (var v in kv.Value)
            {
                if (v is null)
                {
                    continue;
                }

                sb.Append(first ? '?' : '&');
                first = false;
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append('=');
                sb.Append(EscapeValue(v));
            }
        }

        return sb.ToString();
    }

    // A value encoded for the URL, all but its commas: a comma is as much a character of a query value written
    // as it is, and a [QueryParam] list reads `?drivers=ada,theo` instead of `?drivers=ada%2Ctheo`.
    internal static string EscapeValue(string value)
    {
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return Uri.EscapeDataString(value);
        }

        var sb = new StringBuilder(value.Length + 8);
        var rest = value.AsSpan();
        while (comma >= 0)
        {
            sb.Append(Uri.EscapeDataString(rest[..comma])).Append(',');
            rest = rest[(comma + 1)..];
            comma = rest.IndexOf(',');
        }

        return sb.Append(Uri.EscapeDataString(rest)).ToString();
    }
}
