using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     WASM-only showcase page for <see cref="FullscreenDemo" /> (<c>IFullscreen</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("fullscreen")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class FullscreenPage : Component
{
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Fullscreen — Rask",
            "Take an element fullscreen and follow the browser back out of it, from C#, through Rask's IFullscreen wrapper over the Fullscreen API.",
            Routes.FullscreenPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Fullscreen"],
        P.Class("text-ui-muted")[
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
