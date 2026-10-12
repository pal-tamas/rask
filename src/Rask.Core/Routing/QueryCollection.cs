using System.Collections;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

public sealed class QueryCollection : IQueryCollection
{
    public static readonly QueryCollection Empty =
        new(new Dictionary<string, StringValues>(0, StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, StringValues> _store;

    public QueryCollection() : this(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)) { }

#pragma warning disable MA0016 // adopts this exact dictionary, comparer and all, without a copy; the IDictionary overload copies
    public QueryCollection(Dictionary<string, StringValues> store) =>
#pragma warning restore MA0016
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public QueryCollection(IDictionary<string, StringValues> source) =>
        _store = new Dictionary<string, StringValues>(source, StringComparer.OrdinalIgnoreCase);

    public int Count => _store.Count;

    public ICollection<string> Keys => _store.Keys;

    public StringValues this[string key] =>
        _store.TryGetValue(key, out var value) ? value : StringValues.Empty;

    public bool ContainsKey(string key) => _store.ContainsKey(key);

    public bool TryGetValue(string key, out StringValues value) => _store.TryGetValue(key, out value);

    // A key by name, whatever its case, without allocating: the dictionary answers when it compares that
    // way (a parsed address always does), and an adopted one that does not is walked.
    internal bool TryGetIgnoringCase(string key, out StringValues value)
    {
        if (_store.TryGetValue(key, out value))
        {
            return true;
        }

        if (_store.Count == 0 || ReferenceEquals(_store.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var (name, values) in _store)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                value = values;
                return true;
            }
        }

        return false;
    }

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _store.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
