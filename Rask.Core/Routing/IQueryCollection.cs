using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

public interface IQueryCollection : IEnumerable<KeyValuePair<string, StringValues>>
{
    int Count { get; }
    ICollection<string> Keys { get; }
    StringValues this[string key] { get; }
    bool ContainsKey(string key);
    bool TryGetValue(string key, out StringValues value);
}
