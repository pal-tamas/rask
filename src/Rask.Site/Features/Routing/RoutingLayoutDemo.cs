using Rask.Core.Routing;

namespace Rask.Site.Features;

// A layout is a routed component that renders Outlet() where its children should appear.
// Component pages declare [ParentRoute(typeof(RoutingLayoutDemo))] and their templates are
// joined onto the parent's, so /routing-demo/nested/profile matches this layout, then the
// child below renders inside the Outlet. Unique route strings keep this demo from colliding
// with the real showcase routes — this is exactly how ShowcaseLayout hosts every page.
[Route("routing-demo/nested")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class RoutingLayoutDemo : Component
{
    protected override Component? Render() =>
        Div[
            Nav["sidebar"],
            Main[Outlet]   // children render here
        ];
}

// An empty child template ("") means "default child for this layout".
[Route("profile")]
[ParentRoute(typeof(RoutingLayoutDemo))]
public sealed partial class RoutingNestedProfile : Component
{
    // noindex for the same reason as RoutingAboutPage: this is the routing guide's navigation target,
    // not a page anyone should reach from a search result.
    protected override Component? HeadAssets =>
    [
        Title["Routing demo: profile — Rask"],
        Meta.Name("robots").Content("noindex, follow"),
    ];

    protected override Component? Render() =>
        H1["Profile"];
}
