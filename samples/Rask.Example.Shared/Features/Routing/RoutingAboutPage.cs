using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// A page is just a component with a [Route] attribute. A module initializer registers it,
// and the Router() in the App tree renders it when the URL matches. This demo uses a
// unique route string so it can coexist with the real showcase routes.
public sealed partial class RoutingAboutPage : Page
{
    protected override string Route => "routing-demo/about";

    protected override Type? Parent => typeof(ShowcaseLayout);

    protected override Component? Render() =>
        H1["About"];
}
