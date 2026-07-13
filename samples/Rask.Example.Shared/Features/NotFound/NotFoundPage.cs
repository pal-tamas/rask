using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[NotFound]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NotFoundPage(Navigator nav, RouteState route) : Component
{
    protected override Component? Head => Title()["Not found — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Page not found",
            $"No route is registered for {route.Path}. Pick a section from the sidebar — every showcase page is reachable from there."),
        Div(Class: "d-flex gap-2 mt-3")[
            BsButton(Color: BsColor.Primary, OnClick: () => nav.NavigateTo(Routes.GuidesIndexPage()))[
                BsIcon(Name: BsIconName.House, Class: "me-2"), "Back to guides"]
        ]
    ];
}
