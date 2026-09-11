using Rask.Core.Routing;

namespace Rask.Site.Features;

// No [ParentRoute], deliberately. It used to nest under ShowcaseLayout, which was fine while that
// layout was rooted at "/" and therefore covered the whole app. The layout sits at /docs now, so a
// nested catch-all would only answer inside /docs — every unknown URL at the site root would match
// nothing and render an empty document, which is worse than a 404 because nothing says so.
[NotFound]
public sealed partial class NotFoundPage(Navigator nav, RouteState route) : Component
{
    // No canonical, and noindex. This one component answers EVERY unknown URL on the site, so a
    // canonical would point thousands of addresses at one page — and a 404 that a crawler indexes is a
    // page competing in search results with the content the visitor was looking for. Deliberately not
    // PageMeta.For, whose whole job is the canonical this page must not have.
    protected override Component? HeadAssets =>
    [
        Title["Not found — Rask"],
        Meta.Name("robots").Content("noindex, follow"),
    ];

    // Its own <main>, because it no longer has a layout to provide one. Every other page renders inside
    // ShowcaseLayout's; this one answers URLs anywhere on the site, including outside /docs, so it has to
    // carry its own landmark — a document with no <main> is an accessibility failure as well as a page
    // with nothing for a reader's "skip to content" to reach.
    protected override Component? Render() =>
        Main.Class("mx-auto max-w-3xl px-4 py-16")[
            PageHeader
                .Title("Page not found")
                .Lead($"No route is registered for {route.Path}."),
            Div.Class("flex gap-2 flex-wrap items-center mt-3")[
                UiButton.Tone(UiTone.Primary).OnClick(() => nav.NavigateTo(Routes.GuidesIndexPage()))[UiIcon.Name(UiIconName.Home), "Back to guides"]
            ]
        ];
}
