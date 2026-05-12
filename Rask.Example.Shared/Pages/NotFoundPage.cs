using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[NotFound]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NotFoundPage(Navigator nav, RouteState route) : Component
{
    protected override Component Render() =>
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
