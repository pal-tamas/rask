using Rask.Core.Live;

namespace Rask.Core.Routing;

public sealed class Outlet : Component
{
    public override Component Render()
    {
        var ctx = LiveRenderContext.Current
                  ?? throw new InvalidOperationException(
                      "Outlet() must be called inside a Router render tree.");
        return RouteChainRenderer.RenderChainEntry(ctx);
    }
}
