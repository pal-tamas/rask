using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[NotFound]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class NotFoundPage(Navigator nav, RouteState route) : Component
{
    protected override Component? HeadAssets => Title["Not found — Rask"];

    protected override Component? Render() =>
    [
        PageHeader
            .Title("Page not found")
            .Lead($"No route is registered for {route.Path}. Pick a section from the sidebar — every showcase page is reachable from there."),
        Div.Class($"flex gap-2 flex-wrap items-center {"mt-3"}")[
            Button.Type("button").Class(Ui.BtnPrimary).OnClick(() => nav.NavigateTo(Routes.GuidesIndexPage()))[
                Icon.Name(IconName.House).Class("me-2"), "Back to guides"]
        ]
    ];
}
