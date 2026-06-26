using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="VisualViewportDemo" /> (<c>IVisualViewport</c>).</summary>
[Route("browser/visual-viewport")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class VisualViewportPage : Component
{
    protected override RenderResult Head => Title()["Visual viewport — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Visual viewport",
            "Read the actually-visible viewport via IVisualViewport (window.visualViewport) — size, offset, "
            + "and pinch-zoom scale, e.g. to keep an input above the on-screen keyboard. Works on both transports."),
        CodeSample(
            ["VisualViewportDemo.cs"],
            Notes: "GetAsync() returns a VisualViewport snapshot via the __raskApi.visualViewport helper, or "
                + "null where unsupported. Distinct from IScreenInfo (the physical display).",
            Result: VisualViewportDemo())
    ];
}
