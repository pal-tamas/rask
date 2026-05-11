using Rask.Core.Components;
using Rask.Core.Live;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Routing;

internal static class RouteChainRenderer
{
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
        PageBinder.Bind(page, route.Values, route.Query);
        ctx.NotifyParameters(page);
        return page;
    }
}
