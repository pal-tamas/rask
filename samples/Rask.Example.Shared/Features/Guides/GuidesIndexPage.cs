using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// The Guides landing page: the repo's user guides (docs/*.md) rendered on-site, grouped as cards.
// Each card links to /guides/{slug}, where GuidePage renders the markdown with Markdig.
[Route("guides")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GuidesIndexPage : Component
{
    protected override RenderResult Head => Title()["Guides — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Guides",
            "Narrative documentation for the framework — the same guides that ship in the repo's docs/ "
            + "folder, rendered here. Each guide embeds runnable demos inline and reads like a Rails guide, "
            + "with a Chapters index, an on-this-page rail, and prev/next navigation."),
        Div()[GuideCards.Render()]
    ];
}
