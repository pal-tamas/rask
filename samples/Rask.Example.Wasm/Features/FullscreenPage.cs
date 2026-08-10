using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="FullscreenDemo" /> (<c>IFullscreen</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("fullscreen")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class FullscreenPage : Component
{
    protected override Component? Head => Title["Fullscreen — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Fullscreen"],
        P.Class("text-secondary")[
            "Present an element — or the whole page — fullscreen via IFullscreen (the Fullscreen API), ",
            "passing an ElementRef to target one box. WASM-only: requestFullscreen needs a live user ",
            "gesture. Pairs with Orientation — locking the orientation generally requires fullscreen first."
        ],
        CodeSample
            .Files(["FullscreenDemo.cs"])
            .Notes("RequestAsync(ElementRef?) fullscreens that element (or the page when null); ExitAsync "
                + "leaves. Gate on IsSupportedAsync and wrap in try/catch — a request without activation rejects.")
            .Result(FullscreenDemo)
    ];
}
