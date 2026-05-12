using System.Runtime.CompilerServices;
using Rask.Core.Routing;

namespace Rask.Example.Shared;

// Catch-all under the showcase layout: any path that doesn't match a more
// specific route lands here, so the navbar and sidebar still render.
//
// The source generator rejects '{**rest}' templates at compile time
// (RASK003), so we leave [Route] off and register this route manually in
// a module initializer. RouteRegistry.Add appends to the same list the
// generator emits to.
public sealed class NotFoundPage(Navigator nav, RouteState route) : Component
{
    public override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Page not found",
                $"No route is registered for {route.Path}. Pick a section from the sidebar — every showcase page is reachable from there."),
            Div(Class: "d-flex gap-2 mt-3", Children:
            [
                Button(
                    Class: "btn btn-primary",
                    OnClick: () => nav.Navigate("/"),
                    Children: [I(Class: "bi bi-house me-2"), "Back to welcome"])
            ])
        );
}

internal static class NotFoundRouteRegistrar
{
    [ModuleInitializer]
    internal static void Register() =>
        RouteRegistry.Add([new RouteRegistration(typeof(NotFoundPage), "{**rest}", typeof(ShowcaseLayout))]);
}
