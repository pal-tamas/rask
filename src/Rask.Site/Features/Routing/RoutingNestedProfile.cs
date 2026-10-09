using Rask.Core.Routing;

namespace Rask.Site.Features;

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

    // What this page is called. The layout above shows it, in the first HTML already.
    protected override string? PageTitle => "Profile";

    protected override Component? Render() =>
        H1[PageTitle];
}
