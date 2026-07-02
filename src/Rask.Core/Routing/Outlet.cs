using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Outlet : Component
{
    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

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
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Outlet() must be called inside a Router render tree.");
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
