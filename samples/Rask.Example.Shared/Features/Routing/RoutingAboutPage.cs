using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// A page is just a component with a [Route] attribute. A module initializer registers it,
// and the Router() in the App tree renders it when the URL matches. This demo uses a
// unique route string so it can coexist with the real showcase routes.
[Route("routing-demo/about")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class RoutingAboutPage : Component
{
    protected override Component? Render() =>
        H1["About"];
}
