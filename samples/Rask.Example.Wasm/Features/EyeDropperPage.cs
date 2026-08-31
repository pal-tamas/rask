using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="EyeDropperDemo" /> (<c>IEyeDropper</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("eyedropper")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class EyeDropperPage : Component
{
    protected override Component? HeadAssets => Title["EyeDropper — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["EyeDropper"],
        P.Class("text-slate-500 dark:text-slate-400")[
            "Let the user pick a color from anywhere on screen with the system magnifier loupe, via ",
            "IEyeDropper (the EyeDropper API) — handy for a design tool or theme editor. WASM-only: ",
            "open() needs a live user gesture, and it's Chromium-family only at the time of writing."
        ],
        CodeSample
            .Files(["EyeDropperDemo.cs"])
            .Notes("OpenAsync() resolves with the picked sRGB hex (e.g. \"#3366ff\"), or null if the user "
                + "cancels (Escape) — cancellation is not an error. Gate on IsSupportedAsync.")
            .Result(EyeDropperDemo)
    ];
}
