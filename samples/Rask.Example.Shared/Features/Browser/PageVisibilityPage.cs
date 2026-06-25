using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="PageVisibilityDemo" /> (<c>IPageVisibility</c>).</summary>
[Route("browser/visibility")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PageVisibilityPage : Component
{
    protected override RenderResult Head => Title()["Page visibility — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Page visibility",
            "Read whether the page is foreground/visible via IPageVisibility (document.visibilityState)."),
        CodeSample(
            ["PageVisibilityDemo.cs"],
            Notes: "One-shot reads of visibilityState / hidden. Reacting to the visibilitychange event is a later increment.",
            Result: PageVisibilityDemo())
    ];
}
