using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
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
        ctx.NotifyParameters(page, propsChanged);
        return page;
    }
}
