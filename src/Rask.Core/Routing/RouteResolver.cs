namespace Rask.Core.Routing;

public static class RouteResolver
{
    private static readonly object _lock = new();
    private static IReadOnlyList<Route>? _cachedRoots;
    private static IReadOnlyList<RouteLeaf>? _cachedLeaves;

    public static bool TryResolve(string path, out IReadOnlyList<Type> chain)
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

        return RouteMatcher.TryMatch(leaves, path, out chain, out _);
    }
}
