using Rask.Core.Routing;

namespace Rask.Server;

/// <summary>The last path a session resolved to a route, kept so the next event does not match it again.</summary>
/// <remarks>
///     Every event resolves the session's path twice — once to guard the handler, once to guard the render after
///     it — and a match walks the route table allocating per route it tries. A session sits on one path between
///     navigations, so one entry answers nearly all of them. Only the path-to-pages lookup is kept, which the
///     table and the path alone decide; who may see those pages is asked afresh every time.
/// </remarks>
internal sealed class SessionRouteMemo
{
    // Replaced whole, never edited, so a reader on another thread sees one resolution or the other.
    private Resolution? _last;

    public bool TryResolve(IReadOnlyList<Route> table, string path, out IReadOnlyList<Type> chain)
    {
        var last = _last;
        if (last is null || !ReferenceEquals(last.Table, table) || !string.Equals(last.Path, path, StringComparison.Ordinal))
        {
            var matched = RouteResolver.TryResolve(table, path, out var resolved, out _);
            _last = last = new Resolution(table, path, matched, resolved);
        }

        chain = last.Chain;
        return last.Matched;
    }

    private sealed record Resolution(IReadOnlyList<Route> Table, string Path, bool Matched, IReadOnlyList<Type> Chain);
}
