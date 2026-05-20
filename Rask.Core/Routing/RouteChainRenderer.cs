using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
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
    // OnPropsChanged on real URL transitions for the same cached page.
    private static readonly ConditionalWeakTable<Component, PathSnapshot> _lastPath = new();

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Page types reach the route chain through Route.PageType / RouteRegistration.PageType, " +
                        "which are annotated with PublicConstructors | PublicProperties. The generated route " +
                        "registry initialiser also emits a [DynamicDependency(All, typeof(TPage))] per registered " +
                        "page, so ActivatorUtilities.CreateInstance and PageBinder property reflection are safe.")]
    public static Component RenderChainEntry(LiveRenderContext ctx)
    {
        var route = ctx.Route
                    ?? throw new InvalidOperationException(
                        "Outlet() and Router rendering require an active route context. " +
                        "Place Outlet() inside a Router(...) render tree.");

        if (route.Cursor >= route.Chain.Count)
        {
            return new Fragment();
        }

        var type = route.Chain[route.Cursor++];
        var page = ctx.GetOrCreate(type, sp => (Component)ActivatorUtilities.CreateInstance(sp, type));
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

        ctx.NotifyParameters(page, propsChanged);
        return page;
    }

    private sealed class PathSnapshot
    {
        public string Path = string.Empty;
    }
}
