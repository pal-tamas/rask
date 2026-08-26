namespace Rask.Core.Routing;

public static class RouteResolver
{
    private static readonly object _lock = new();
    private static IReadOnlyList<Route>? _cachedRoots;
    private static IReadOnlyList<RouteLeaf>? _cachedLeaves;

    public static bool TryResolve(string path, out IReadOnlyList<Type> chain)
        => TryResolve(path, out chain, out _);

    /// <summary>
    ///     Resolve <paramref name="path" />, additionally reporting whether it fell through to the
    ///     not-found page. A host uses this to answer a real <c>404</c>: the catch-all matches, so
    ///     resolution succeeds and the chain is perfectly renderable — "nothing here" is a fact
    ///     about which route won, not about whether one did.
    /// </summary>
    public static bool TryResolve(string path, out IReadOnlyList<Type> chain, out bool isNotFound)
    {
        var roots = RouteRegistry.BuildTree();
        IReadOnlyList<RouteLeaf> leaves;
        lock (_lock)
        {
            if (!ReferenceEquals(_cachedRoots, roots) || _cachedLeaves is null)
            {
                _cachedRoots = roots;
                _cachedLeaves = RouteFlattener.Flatten(roots);
            }

            leaves = _cachedLeaves;
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
