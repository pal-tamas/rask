using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>
///     Where a layout renders its current child route. Put one in a layout at the spot the page content
///     belongs, and the layout's chrome — header, nav, footer — stays mounted across navigations while
///     only the outlet's contents change.
/// </summary>
public sealed class Outlet : Component
{
    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    // Render() advances RouteRenderState.Cursor, which is frame-global: each Outlet takes the next
    // link of the chain in walk order. A cached Outlet does not advance it, so the next one to
    // render reads a cursor short by one and pulls the WRONG page — its own parent, nested inside
    // itself. Router carries the matching note and the rest of the reasoning.
    protected override bool BypassRenderCache => true;

    protected override void OnMount()
    {
        // Subscribe to RouteState.Changed so the cached subtree is invalidated when the
        // route chain changes. Without this, Router's re-render would walk past a cached
        // Outlet whose ctx.Route snapshot is stale.
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null)
        {
            return;
        }

        _route.Changed += StateHasChanged;
    }

    protected override void OnUnmount()
    {
        if (_route is null)
        {
            return;
        }

        _route.Changed -= StateHasChanged;
    }

    protected override Component? Render()
    {
        // Same condition as RouteChainRenderer.RenderChainEntry, and deliberately the same words: which
        // of the two you hit depends only on whether there is a live render context or merely no route
        // in it, which is not a distinction the reader can act on. Two spellings for one problem meant
        // searching for the message found half the story.
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Outlet() and Router rendering require an active route context. " +
                      "Place Outlet() inside a Router(...) render tree.");
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
