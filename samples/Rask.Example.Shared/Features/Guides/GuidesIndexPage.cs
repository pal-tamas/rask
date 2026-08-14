using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// The site root: the repo's user guides (docs/*.md) rendered on-site, grouped as cards. Guides-first, so
// this is served at "/" (the old Welcome landing page is gone). Each card links to /guides/{slug}, where
// GuidePage renders the markdown with Markdig.
[Route("")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class GuidesIndexPage : Component
{
    protected override Component? HeadAssets => Title["Guides — Rask"];

    protected override Component? Render() =>
    [
        PageHeader
            .Title("Guides")
            .Lead("Narrative documentation for the framework — the same guides that ship in the repo's docs/ "
                  + "folder, rendered here. Each guide embeds runnable demos inline and reads like a proper "
                  + "narrative guide, with a Chapters index, an on-this-page rail, and prev/next navigation."),
        Div[GuideCards]
    ];
}
