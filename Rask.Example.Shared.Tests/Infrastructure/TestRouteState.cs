using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Tests.Infrastructure;

// Convenience helpers for constructing/mutating RouteState in tests. The real type
// has public setters on Path/Query (they fire .Changed), so we don't subclass —
// just provide ergonomic builders.
internal static class TestRouteState
{
    public static RouteState At(string path)
    {
        var rs = new RouteState();
        rs.Path = path;
        return rs;
    }

    public static RouteState At(string path, IReadOnlyDictionary<string, string> query)
    {
        var rs = new RouteState();
        rs.Path = path;
        var dict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in query)
        {
            dict[kv.Key] = kv.Value;
        }

        rs.Query = new QueryCollection(dict);
        return rs;
    }
}
