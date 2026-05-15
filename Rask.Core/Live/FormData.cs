using System.Collections;
using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Core.Live;

public sealed class FormData : IReadOnlyDictionary<string, string>
{
    private static readonly IReadOnlyList<RaskFile> EmptyFiles = Array.Empty<RaskFile>();

    private readonly IReadOnlyDictionary<string, IReadOnlyList<RaskFile>> _files;
    private readonly IReadOnlyDictionary<string, string> _values;

    public FormData(IReadOnlyDictionary<string, string> values)
        : this(values, new Dictionary<string, IReadOnlyList<RaskFile>>())
    {
    }

    public FormData(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<RaskFile>> files)
    {
        _values = values;
        _files = files;
    }

    public string this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<string> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public string Get(string key) => _values.TryGetValue(key, out var v) ? v : string.Empty;

    public IReadOnlyList<RaskFile> Files(string key) =>
        _files.TryGetValue(key, out var f) ? f : EmptyFiles;

    public bool HasFiles(string key) => _files.TryGetValue(key, out var f) && f.Count > 0;

    public IEnumerable<string> FileKeys => _files.Keys;

    internal static FormData FromJson(JsonElement payload)
    {
        var dict = new Dictionary<string, string>();
        var fileDict = new Dictionary<string, IReadOnlyList<RaskFile>>();
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("form", out var form)
            && form.ValueKind == JsonValueKind.Object)
        {
            var backend = FileListReader.ResolveBackend();
            foreach (var entry in form.EnumerateObject())
            {
                if (entry.Name == "__files")
                {
                    if (backend is null || entry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var fileField in entry.Value.EnumerateObject())
                    {
                        if (fileField.Value.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        var list = new List<RaskFile>(fileField.Value.GetArrayLength());
                        foreach (var meta in fileField.Value.EnumerateArray())
                        {
                            if (meta.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            list.Add(backend.Create(meta));
                        }

                        fileDict[fileField.Name] = list;
                    }

                    continue;
                }

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

        return new FormData(dict, fileDict);
    }

    internal IReadOnlyDictionary<string, IReadOnlyList<RaskFile>> FilesByField => _files;
}
