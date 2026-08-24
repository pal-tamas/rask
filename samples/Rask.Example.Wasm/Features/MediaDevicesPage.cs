using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="MediaDevicesDemo" /> (<c>IMediaDevices</c>). Surfaced in the
///     shared sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("media-devices")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class MediaDevicesPage : Component
{
    protected override Component? HeadAssets => Title["Camera & microphone — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Camera & microphone"],
        P.Class("text-secondary")[
            "Capture the camera, microphone, or screen and show it in a <video> via IMediaDevices ",
            "(getUserMedia / getDisplayMedia) — for photo capture, video calls, or screen recording. ",
            "WASM-only: capture needs a live user gesture and a secure context. Dispose the stream handle ",
            "to stop every track and release the hardware (the camera indicator turns off)."
        ],
        CodeSample
            .Files(["MediaDevicesDemo.cs"])
            .Notes("GetUserMediaAsync(constraints) / GetDisplayMediaAsync() return a disposable "
                + "IMediaStreamHandle; AttachToAsync(ElementRef) wires the stream to a <video> and plays it. "
                + "The live MediaStream stays JS-side under a minted id. Gate on IsSupportedAsync and "
                + "try/catch — a denied request throws.")
            .Result(MediaDevicesDemo)
    ];
}
