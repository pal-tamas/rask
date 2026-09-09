using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Site.Features;

// Renders one guide: docs/{slug}.md, embedded and read by GuideCatalog. The slug comes straight from the
// route, so /guides/routing renders docs/routing.md. The narrative-guide layout — Chapters TOC, the
// prose (with any inline demos), a sticky on-this-page rail, and prev/next — all lives in GuideChrome;
// this page is just the routed shell that supplies the slug and the document title.
[Route("guides/{slug}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class GuidePage : Component
{
    [RouteParam] public string Slug { get; set; } = string.Empty;

    // Built from the SLUG rather than from a constant, so each of the ~80 guides carries its own title,
    // description and canonical. The canonical matters more here than anywhere else on the site: a guide
    // is the page most likely to be linked with a fragment or a tracking parameter, and without one a
    // crawler treats every variant as a separate page competing with the others.
    protected override Component? HeadAssets =>
        PageMeta.For(
            $"{GuideCatalog.TitleFor(Slug)} — Guides — Rask",
            GuideCatalog.BlurbFor(Slug),
            Routes.GuidePage(Slug));

    protected override Component? Render() => GuideChrome.Slug(Slug);
}
