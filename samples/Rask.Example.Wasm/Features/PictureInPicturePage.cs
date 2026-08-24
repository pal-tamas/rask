using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="PictureInPictureDemo" /> (<c>IPictureInPicture</c>). Surfaced
///     in the shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("picture-in-picture")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class PictureInPicturePage : Component
{
    protected override Component? HeadAssets => Title["Picture-in-Picture — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Picture-in-Picture"],
        P.Class("text-secondary")[
            "Float a video into an always-on-top miniplayer the user keeps visible while they scroll or ",
            "switch tabs, via IPictureInPicture (the Picture-in-Picture API). WASM-only: ",
            "requestPictureInPicture needs a live user gesture. This demo synthesizes its video from an ",
            "animated canvas (sibling scoped JS), so it needs no shipped media file."
        ],
        CodeSample
            .Files(["PictureInPictureDemo.cs", "PictureInPictureDemo.js"])
            .Notes("RequestAsync(ElementRef) sends that <video> to the miniplayer; ExitAsync brings it back. "
                + "Gate on IsSupportedAsync and wrap in try/catch — a request without activation rejects.")
            .Result(PictureInPictureDemo)
    ];
}
