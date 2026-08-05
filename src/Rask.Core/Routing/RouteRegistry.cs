using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Routing;

public static class RouteRegistry
{
    internal const string DefaultFallbackTemplate = "{**__rask_notfound}";

    private static readonly object _lock = new();

    // Registrations from direct Add() calls. Additive, exactly as before.
    private static readonly List<RouteRegistration> _manual = new();

    // Registrations owned by a keyed contributor — one group per assembly's generated
    // __RaskRoutesRegistry, which passes its own typeof(...) as the key. Grouping is what makes
    // routes hot-reloadable: a refresh Replace()s just that assembly's set, so re-running it
    // neither duplicates its own routes (Add is AddRange) nor drops another assembly's, and never
    // touches _defaultFallback — which is seeded once by __RaskDefaultFallback's [ModuleInitializer]
    // and could not be restored if a refresh cleared it.
    private static readonly List<(object Key, RouteRegistration[] Items)> _groups = new();

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                DynamicallyAccessedMemberTypes.PublicProperties)]
    private static Type? _defaultFallback;

    private static IReadOnlyList<Route>? _treeCache;

    public static void Add(IEnumerable<RouteRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        lock (_lock)
        {
            _manual.AddRange(registrations);
            _treeCache = null;
        }
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as the complete set owned by
    ///     <paramref name="groupKey" />, replacing any set previously registered under that key.
    ///     Generated per-assembly initializers call this (passing their own
    ///     <c>typeof(__RaskRoutesRegistry)</c>), so re-running one under hot reload swaps that
    ///     assembly's routes — picking up added, edited and deleted <c>[Route]</c> templates —
    ///     while leaving every other contributor, direct <see cref="Add" /> calls, and the
    ///     default fallback untouched.
    ///     <para>
    ///         The key is compared by reference and is never used for reflection, so it attracts
    ///         no trimmer analysis.
    ///     </para>
    /// </summary>
    public static void Replace(object groupKey, IEnumerable<RouteRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as RouteRegistration[] ?? registrations.ToArray();
        lock (_lock)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                if (!ReferenceEquals(_groups[i].Key, groupKey))
                {
                    continue;
                }

                // An unrelated hot reload re-runs every RefreshAll(); skip the tree rebuild when
                // this contributor's routes are unchanged.
                if (_groups[i].Items.AsSpan().SequenceEqual(items))
                {
                    return;
                }

                _groups[i] = (groupKey, items);
                _treeCache = null;
                return;
            }

            _groups.Add((groupKey, items));
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

            var effective = Flatten();
            if (!HasCatchAll(effective) && _defaultFallback is not null)
            {
                effective.Add(new RouteRegistration(_defaultFallback, DefaultFallbackTemplate, null));
            }

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
            _manual.Clear();
            _groups.Clear();
            _defaultFallback = null;
            _treeCache = null;
        }
    }

    // Direct Add() registrations first, then each keyed group in the order it first registered.
    // Route matching sorts by specificity (RouteFlattener), not by this order, so the only thing
    // it decides is how equal-specificity ties fall — already unspecified. Caller holds _lock.
    private static List<RouteRegistration> Flatten()
    {
        var total = _manual.Count;
        foreach (var group in _groups)
        {
            total += group.Items.Length;
        }

        var all = new List<RouteRegistration>(total + 1);
        all.AddRange(_manual);
        foreach (var group in _groups)
        {
            all.AddRange(group.Items);
        }

        return all;
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
}
