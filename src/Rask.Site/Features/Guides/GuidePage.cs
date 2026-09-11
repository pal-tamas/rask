using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Site.Features;

// Renders one guide: docs/{slug}.md, embedded and read by GuideCatalog. The slug comes straight from the
// route, so /guides/routing renders docs/routing.md. The narrative-guide layout — Chapters TOC, the
// prose (with any inline demos), a sticky on-this-page rail, and prev/next — all lives in GuideChrome;
// this page is just the routed shell that supplies the slug and the document head.
[Route("guides/{slug}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class GuidePage : Component
{
    [RouteParam] public string Slug { get; set; } = string.Empty;

    // Built from the catalog ENTRY rather than from a constant, so each of the ~130 guides carries its own
    // title, description and canonical. The canonical matters more here than anywhere else on the site: a
    // guide is the page most likely to be linked with a fragment or a tracking parameter, and without one a
    // crawler treats every variant as a separate page competing with the others.
    //
    // The title and description are the entry's SEARCH copy, not its sidebar title and card blurb. Those are
    // written for someone already on the site and were four words long; a search result is read by someone
    // who is not, and "IBattery — Guides — Rask" matched nothing anyone types.
    //
    // And it is an article: a section, the date git last saw its source change, and a Markdown twin.
    protected override Component? HeadAssets =>
        GuideCatalog.Find(Slug) is { } guide
            ? PageMeta.For(
                guide.SearchTitle + PageMeta.TitleSuffix,
                guide.Description,
                Routes.GuidePage(Slug),
                new PageArticle(guide.Group, GuideHistory.LastModified(Slug), LlmsText.MarkdownUrl(Slug)))
            : PageMeta.For(
                Slug + PageMeta.TitleSuffix,
                "There is no guide by this name. The Rask guides cover components, routing, forms, data, auth, "
                + "background work and deployment in C#.",
                Routes.GuidePage(Slug));

    protected override Component? Render() => GuideChrome.Slug(Slug);
}
