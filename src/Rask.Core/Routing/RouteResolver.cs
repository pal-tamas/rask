namespace Rask.Core.Routing;

public static class RouteResolver
{
    private static readonly object _lock = new();

    // Flattened leaves per route tree, keyed by the tree's IDENTITY. This was a single pair of fields
    // until mounted applications existed, when one host began resolving against two trees — the host
    // application's and the console's — and a one-entry cache re-flattened on EVERY request as the two
    // alternated. Reference-keyed, because RouteRegistry hands back a cached instance per tree and mints
    // a new one whenever the table changes; a stale tree therefore drops out of this table on its own
    // once nothing holds it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        IReadOnlyList<Route>, IReadOnlyList<RouteLeaf>> _leavesByTree = new();

    public static bool TryResolve(string path, out IReadOnlyList<Type> chain)
        => TryResolve(path, out chain, out _);

    /// <summary>
    ///     Resolve <paramref name="path" />, additionally reporting whether it fell through to the
    ///     not-found page. A host uses this to answer a real <c>404</c>: the catch-all matches, so
    ///     resolution succeeds and the chain is perfectly renderable — "nothing here" is a fact
    ///     about which route won, not about whether one did.
    /// </summary>
    public static bool TryResolve(string path, out IReadOnlyList<Type> chain, out bool isNotFound) =>
        TryResolve(RouteRegistry.BuildTree(), path, out chain, out isNotFound);

    /// <summary>
    ///     Resolve <paramref name="path" /> against a GIVEN route table rather than the whole registry.
    /// </summary>
    /// <remarks>
    ///     A host serving a mounted application resolves against that application's own table, so the
    ///     console at <c>/_rask</c> cannot reach the host's pages and the host cannot reach the console's.
    ///     Passing the table in rather than a flag keeps this a pure function of it, which is what lets
    ///     the leaves be cached by tree identity.
    /// </remarks>
    public static bool TryResolve(
        IReadOnlyList<Route> roots,
        string path,
        out IReadOnlyList<Type> chain,
        out bool isNotFound)
    {
        ArgumentNullException.ThrowIfNull(roots);

        IReadOnlyList<RouteLeaf> leaves;
        lock (_lock)
        {
            if (!_leavesByTree.TryGetValue(roots, out var cached))
            {
                cached = RouteFlattener.Flatten(roots);
                _leavesByTree.Add(roots, cached);
            }

            leaves = cached;
        }

        var matched = RouteMatcher.TryMatch(leaves, path, out chain, out _, out var fullTemplate);
        // A path that matched the reserved catch-all fell through; one that matched nothing has
        // nothing at it either. Both are candidates for a 404 — but only candidates: whether the
        // user is actually looking at the not-found page depends on the app mounting a Router at
        // all, which the route table cannot know. The host confirms it against the render.
        isNotFound = !matched || RouteRegistry.IsFallbackTemplate(fullTemplate);
        return matched;
    }
}
