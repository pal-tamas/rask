using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Outlet : Component
{
    // Outlet reads ctx.Route (the per-render route chain state). Like Router, opt out of
    // the render cache so route changes flow through the chain on every render.
    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Outlet() must be called inside a Router render tree.");
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
