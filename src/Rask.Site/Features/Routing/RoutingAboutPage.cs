using Rask.Core.Routing;

namespace Rask.Site.Features;

// A page is just a component with a [Route] attribute. A module initializer registers it,
// and the Router() in the App tree renders it when the URL matches. This demo uses a
// unique route string so it can coexist with the real showcase routes.
[Route("routing-demo/about")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class RoutingAboutPage : Component
{
    // Scaffolding, not content: this page exists so the routing guide has somewhere to navigate TO, and
    // its whole body is the word "About". noindex keeps it out of search results, and the prerender pass
    // reads that back off the rendered page to keep it out of sitemap.xml too — a sitemap that lists a
    // noindex URL is a contradiction Search Console reports against the whole file.
    protected override Component? HeadAssets =>
    [
        Title["Routing demo: about — Rask"],
        Meta.Name("robots").Content("noindex, follow"),
    ];

    protected override Component? Render() =>
        H1["About"];
}
