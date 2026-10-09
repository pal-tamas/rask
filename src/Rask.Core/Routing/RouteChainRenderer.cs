using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

internal static class RouteChainRenderer
{
    // Multi-route pages can switch URLs without changing any [RouteParam] (e.g.,
    // `/todos` ↔ `/todos/new` both bind to the same TodosPage with Id=null). In that
    // case PageBinder.Bind alone reports propsChanged=false, the render cache returns
    // the stale prior result, and consumers that derive UI state from RouteState.Path
    // never see the transition. Snapshotting the last URL per page instance — and OR-ing
    // path change into the propsChanged signal — invalidates the cache and refires
    // Updated on real URL transitions for the same cached page.
    private static readonly ConditionalWeakTable<Component, PathSnapshot> _lastPath = new();

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Page types reach the route chain through Route.PageType / RouteRegistration.PageType, " +
                        "which are annotated with PublicConstructors | PublicProperties. The generated route " +
                        "registry initialiser also emits a [DynamicDependency(All, typeof(TPage))] per registered " +
                        "page, so ActivatorUtilities.CreateInstance and PageBinder property reflection are safe.")]
    public static Component RenderChainEntry(LiveRenderContext ctx)
    {
        var route = ctx.Route is { Pages: not null } matched
            ? matched
            : throw new InvalidOperationException(
                        "Outlet and Router rendering require an active route context. " +
                        "Place Outlet inside a Router render tree.");

        if (route.Cursor >= route.Chain.Count)
        {
            return new Fragment();
        }

        // Created HERE, where the layout placed the outlet, and nowhere earlier: a layout that withholds
        // its Outlet keeps the page from being constructed. The instance itself is the router's to
        // remember (RoutePageInstances), so it outlives an outlet its layout re-creates.
        var index = route.Cursor++;
        var page = route.Pages!.At(index, route.Chain[index], route.Chain.Count, ctx);
        var propsChanged = PageBinder.Bind(page, route.Values, route.Query);
        if (_lastPath.TryGetValue(page, out var snapshot))
        {
            if (!string.Equals(snapshot.Path, route.Path, StringComparison.Ordinal))
            {
                propsChanged = true;
                snapshot.Path = route.Path;
            }
        }
        else
        {
            _lastPath.Add(page, new PathSnapshot { Path = route.Path });
        }

        LiveRenderContext.NotifyParameters(page, propsChanged);

        // Read on every frame, after the hooks: a title built from the page's own state follows it. The
        // chain is walked layout first, so the deepest page that declares one is the last to write.
        if (route.Title is { } routeTitle && page.PageTitleInternal is { } title)
        {
            routeTitle.Offer(title);
        }

        return page;
    }

    private sealed class PathSnapshot
    {
        public string Path = string.Empty;
    }
}
