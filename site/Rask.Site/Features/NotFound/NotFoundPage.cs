using Rask.Core.Routing;

namespace Rask.Site.Features;

// No [ParentRoute], deliberately. It used to nest under ShowcaseLayout, which was fine while that
// layout was rooted at "/" and therefore covered the whole app. The layout sits at /docs now, so a
// nested catch-all would only answer inside /docs — every unknown URL at the site root would match
// nothing and render an empty document, which is worse than a 404 because nothing says so.
[NotFound]
public sealed partial class NotFoundPage(Navigator nav, RouteState route) : Component
{
    protected override Component? HeadAssets => Title["Not found — Rask"];

    protected override Component? Render() =>
    [
        PageHeader
            .Title("Page not found")
            .Lead($"No route is registered for {route.Path}."),
        Div.Class("flex gap-2 flex-wrap items-center mt-3")[
            Button.Type("button").Class(Tw.BtnPrimary).OnClick(() => nav.NavigateTo(Routes.GuidesIndexPage()))[
                UiIcon.Name(UiIconName.Home).Class("me-2"), "Back to the guides"]
        ]
    ];
}
