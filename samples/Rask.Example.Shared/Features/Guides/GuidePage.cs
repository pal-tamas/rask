using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Renders one guide: docs/{slug}.md, embedded and read by GuideCatalog. The slug comes straight from the
// route, so /guides/routing renders docs/routing.md. The narrative-guide layout — Chapters TOC, the
// prose (with any inline demos), a sticky on-this-page rail, and prev/next — all lives in GuideChrome;
// this page is just the routed shell that supplies the slug and the document title.
public sealed partial class GuidePage : Page
{
    protected override string Route => "guides/{slug}";

    protected override Type? Parent => typeof(ShowcaseLayout);

    [RouteParam] public string Slug { get; set; } = string.Empty;

    protected override Component? HeadAssets => Title[$"{GuideCatalog.TitleFor(Slug)} — Guides — Rask"];

    protected override Component? Render() => GuideChrome.Slug(Slug);
}
