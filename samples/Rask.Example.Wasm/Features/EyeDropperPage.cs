using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="EyeDropperDemo" /> (<c>IEyeDropper</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("eyedropper")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class EyeDropperPage : Component
{
    protected override RenderResult Head => Title()["EyeDropper — Rask"];

    protected override RenderResult Render() =>
    [
        H1(Class: "h2 mb-1")["EyeDropper"],
        P(Class: "text-secondary")[
            "Let the user pick a color from anywhere on screen with the system magnifier loupe, via ",
            "IEyeDropper (the EyeDropper API) — handy for a design tool or theme editor. WASM-only: ",
            "open() needs a live user gesture, and it's Chromium-family only at the time of writing."
        ],
        CodeSample(
            ["EyeDropperDemo.cs"],
            Notes: "OpenAsync() resolves with the picked sRGB hex (e.g. \"#3366ff\"), or null if the user "
                + "cancels (Escape) — cancellation is not an error. Gate on IsSupportedAsync.",
            Result: EyeDropperDemo())
    ];
}
