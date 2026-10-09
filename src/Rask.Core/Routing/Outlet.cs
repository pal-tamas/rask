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
    // Unmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    // Render() advances RouteRenderState.Cursor, which is frame-global: each Outlet places the next
    // page of the chain — already mounted by the Router — in walk order. A cached Outlet does not
    // advance it, so the next one to render reads a cursor short by one and pulls the WRONG page — its
    // own parent, nested inside itself. Router carries the matching note and the rest of the reasoning.
    protected override bool BypassRenderCache => true;

    protected override Task OnMount()
    {
        // Subscribe to RouteState.Changed so the cached subtree is invalidated when the
        // route chain changes. Without this, Router's re-render would walk past a cached
        // Outlet whose ctx.Route snapshot is stale.
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null)
        {
            return Task.CompletedTask;
        }

        _route.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        if (_route is null)
        {
            return Task.CompletedTask;
        }

        _route.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Component? Render()
    {
        // One message for both ways of being outside a router — no live render at all, or one in which no
        // Router has rendered — because the difference is not one the reader can act on.
        var route = LiveRenderContext.Current?.Route
                    ?? throw new InvalidOperationException(
                        "Outlet and Router rendering require an active route context. " +
                        "Place Outlet inside a Router render tree.");
        return route.NextPage();
    }
}
