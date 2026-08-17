using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// A layout is a routed component that renders Outlet() where its children should appear.
// Component pages declare [ParentRoute(typeof(RoutingLayoutDemo))] and their templates are
// joined onto the parent's, so /routing-demo/nested/profile matches this layout, then the
// child below renders inside the Outlet. Unique route strings keep this demo from colliding
// with the real showcase routes — this is exactly how ShowcaseLayout hosts every page.
public sealed partial class RoutingLayoutDemo : Page
{
    protected override string Route => "routing-demo/nested";

    protected override Type? Parent => typeof(ShowcaseLayout);

    protected override Component? Render() =>
        Div[
            Nav["sidebar"],
            Main[Outlet]   // children render here
        ];
}

// An empty child template ("") means "default child for this layout".
public sealed partial class RoutingNestedProfile : Page
{
    protected override string Route => "profile";

    protected override Type? Parent => typeof(RoutingLayoutDemo);

    protected override Component? Render() =>
        H1["Profile"];
}
