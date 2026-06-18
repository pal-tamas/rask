using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Live;
using F = Rask.Core.Components.Generated;

namespace Rask.Core.Routing;

public sealed class Outlet : Component
{
    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    // The default per-outlet boundary. Reused positionally across renders, so once it trips
    // it stays tripped until cleared — we Recover() it on every navigation so a crash on one
    // page doesn't "stick" over the next (healthy) page rendered into this same outlet.
    private ErrorBoundary? _boundary;

    // Opt out of the default per-outlet error boundary. Nullable so the generated factory
    // exposes it as the optional parameter `Outlet(bool? DisableErrorBoundary = null)`:
    // null/false ⇒ boundary ON (the default), only true lets a child fault bubble outward.
    public bool? DisableErrorBoundary { get; set; }

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

        _route.Changed += OnRouteChanged;
    }

    protected override void OnUnmount()
    {
        if (_route is null)
        {
            return;
        }

        _route.Changed -= OnRouteChanged;
    }

    private void OnRouteChanged()
    {
        // Give the next route a clean attempt: clear any tripped state left by the previous
        // page so the boundary doesn't keep rendering the old error over the new page. The
        // default boundary is a navigate-away safety net — for in-place retry, wrap the part
        // that can fail in an explicit ErrorBoundary with a Recover() button.
        _boundary?.Recover();
        StateHasChanged();
    }

    protected override RenderResult Render()
    {
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Outlet() must be called inside a Router render tree.");

        // Render the matched chain entry first so the route cursor advances identically
        // whether or not we wrap it — preserving the empty-leaf and two-outlets-in-one-render
        // semantics that RouteChainRenderer's cursor relies on.
        var entry = RouteChainRenderer.RenderChainEntry(ctx);

        if (DisableErrorBoundary == true)
        {
            return entry;
        }

        // By default, contain a child page's render fault to this outlet region: the
        // surrounding layout (nav/sidebar) stays live and the framework DefaultErrorPage
        // renders here, instead of the fault bubbling to the RootErrorBoundary and replacing
        // the whole page shell. Opt out with Outlet(DisableErrorBoundary: true).
        var boundary = F.ErrorBoundary();
        _boundary = boundary;
        return boundary[entry];
    }
}
