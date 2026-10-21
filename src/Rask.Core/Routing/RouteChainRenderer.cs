using System.Diagnostics.CodeAnalysis;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core.Routing;

internal static class RouteChainRenderer
{
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

        // The page the person may not open: its layouts above are already drawn, and the sentence stands where it
        // would have. Nothing below it is reached, so no outlet further down pulls a page of the chain.
        if (route.Chain[index] == route.Forbidden)
        {
            route.Cursor = route.Chain.Count;
            return new ForbiddenPage();
        }

        var page = route.Pages!.At(index, route.Chain[index], route.Chain.Count, ctx);
        // The path goes in with the values: a multi-route page can change address without changing a
        // property (`/todos` ↔ `/todos/new`, Id null on both), and whatever it derives from RouteState.Path
        // must still be rendered again and hear Updated.
        var propsChanged = PageBinder.Bind(page, route.Values, route.Query, route.Path);

        LiveRenderContext.NotifyParameters(page, propsChanged);

        // Read on every frame, after the hooks: a title built from the page's own state follows it. The
        // chain is walked layout first, so the deepest page that declares one is the last to write.
        if (route.Title is { } routeTitle && page.PageTitleInternal is { } title)
        {
            routeTitle.Offer(title);
        }

        return page;
    }
}
