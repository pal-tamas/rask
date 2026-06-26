using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="ScreenInfoDemo" /> (<c>IScreenInfo</c>).</summary>
[Route("browser/screen")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ScreenInfoPage : Component
{
    protected override RenderResult Head => Title()["Screen info — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Screen info",
            "Read the display size, color depth, and device pixel ratio via IScreenInfo (window.screen) — "
            + "e.g. to pick retina image resolution or for analytics. Works on both transports."),
        CodeSample(
            ["ScreenInfoDemo.cs"],
            Notes: "GetAsync() returns a ScreenInfo snapshot via the framework's __raskApi.screen helper. "
                + "Re-read for a fresh value (e.g. after moving the window between displays).",
            Result: ScreenInfoDemo())
    ];
}
