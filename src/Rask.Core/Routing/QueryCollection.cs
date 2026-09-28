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

    public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() => _store.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
