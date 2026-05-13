using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Routing;

public static class RouteRegistry
{
    internal const string DefaultFallbackTemplate = "{**__rask_notfound}";

    private static readonly object _lock = new();
    private static readonly List<RouteRegistration> _registrations = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                DynamicallyAccessedMemberTypes.PublicProperties)]
    private static Type? _defaultFallback;

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

    public static void SetDefaultFallback(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                    DynamicallyAccessedMemberTypes.PublicProperties)]
        Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);
        lock (_lock)
        {
            _defaultFallback = pageType;
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

            var effective = HasCatchAll(_registrations) || _defaultFallback is null
                ? (IReadOnlyList<RouteRegistration>)_registrations
                : Append(_registrations, new RouteRegistration(_defaultFallback, DefaultFallbackTemplate, null));

            var byParent = effective.ToLookup(r => r.Parent);

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
            _defaultFallback = null;
            _treeCache = null;
        }
    }

    private static bool HasCatchAll(List<RouteRegistration> registrations)
    {
        // String check is sufficient: RoutePattern accepts both "{*name}" and "{**name}"
        // as catch-alls, and both contain the "{*" sequence. Literal segments that happen
        // to contain "{*" elsewhere aren't valid route templates.
        foreach (var r in registrations)
        {
            if (r.Template.Contains("{*", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<RouteRegistration> Append(List<RouteRegistration> source, RouteRegistration extra)
    {
        var copy = new List<RouteRegistration>(source.Count + 1);
        copy.AddRange(source);
        copy.Add(extra);
        return copy;
    }
}
