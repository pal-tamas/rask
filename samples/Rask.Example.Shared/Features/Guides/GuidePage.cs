using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Renders one guide: docs/{slug}.md, embedded and read by GuideCatalog. The slug comes straight from the
// route, so /guides/routing renders docs/routing.md. The Rails-guides-style layout — Chapters TOC, the
// prose (with any inline demos), a sticky on-this-page rail, and prev/next — all lives in GuideChrome;
// this page is just the routed shell that supplies the slug and the document title.
[Route("guides/{slug}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GuidePage : Component
{
    [RouteParam] public string Slug { get; set; } = string.Empty;

    protected override RenderResult Head => Title()[$"{GuideCatalog.TitleFor(Slug)} — Guides — Rask"];

    protected override RenderResult Render() => GuideChrome(Slug: Slug);
}
