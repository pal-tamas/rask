using System.Collections;
using System.Text.Json;

namespace Rask.Core.Live;

public sealed class FormData : IReadOnlyDictionary<string, string>
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public FormData(IReadOnlyDictionary<string, string> values) => _values = values;

    public string this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<string> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public string Get(string key) => _values.TryGetValue(key, out var v) ? v : string.Empty;

    internal static FormData FromJson(JsonElement payload)
    {
        var dict = new Dictionary<string, string>();
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("form", out var form)
            && form.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in form.EnumerateObject())
            {
                dict[entry.Name] = entry.Value.ValueKind switch
                {
                    JsonValueKind.String => entry.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => entry.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => string.Empty,
                    _ => entry.Value.GetRawText()
                };
            }
        }

        return new FormData(dict);
    }
}
