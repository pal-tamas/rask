using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[NotFound]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NotFoundPage(Navigator nav, RouteState route) : Component
{
    protected override RenderResult Head => Title()["Not found — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Page not found",
            $"No route is registered for {route.Path}. Pick a section from the sidebar — every showcase page is reachable from there."),
        Div(Class: "d-flex gap-2 mt-3")[
            Button(
                Class: "btn btn-primary",
                OnClick: () => nav.NavigateTo("/"))[I(Class: "bi bi-house me-2"), "Back to welcome"]
        ]
    ];
}
