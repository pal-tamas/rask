namespace Rask.Core.Routing;

public static class RouteRegistry
{
    private static readonly object _lock = new();
    private static readonly List<RouteRegistration> _registrations = new();
    private static IReadOnlyList<Route>? _treeCache;

    public static void Add(IEnumerable<RouteRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        lock (_lock)
        {
            _registrations.AddRange(registrations);
            _treeCache = null;
        }
    }

    public static IReadOnlyList<Route> BuildTree()
    {
        lock (_lock)
        {
            if (_treeCache is not null)
            {
                return _treeCache;
            }

            var byParent = _registrations.ToLookup(r => r.Parent);

            Route Build(RouteRegistration r) =>
                new(r.PageType, r.Template, byParent[r.PageType].Select(Build).ToArray());

            _treeCache = byParent[null].Select(Build).ToArray();
            return _treeCache;
        }
    }

    internal static void Reset()
    {
        lock (_lock)
        {
            _registrations.Clear();
            _treeCache = null;
        }
    }
}
