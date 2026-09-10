using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     WASM-only showcase page for <see cref="UsbDemo" /> (<c>IUsb</c>). Surfaced in the shared sidebar via a
///     host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("usb")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class UsbPage : Component
{
    protected override Component? HeadAssets =>
        PageMeta.For(
            "WebUSB — Rask",
            "Pair with and drive a USB device from C# — read its descriptor, claim an interface, run bulk, interrupt and control transfers — through Rask's IUsb wrapper over the WebUSB API.",
            Routes.UsbPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["WebUSB"],
        P.Class("text-ui-muted")[
            "Pair with and drive a USB device — custom hardware, a dev board, an instrument — straight from C# ",
            "via IUsb (the WebUSB API): show its descriptor, open it, claim an interface, and run ",
            "bulk / interrupt / control transfers. WASM-only: requestDevice() needs a live user gesture and the ",
            "live device handle, and it's Chromium-family only at the time of writing."
        ],
        CodeSample
            .Files(["UsbDemo.cs"])
            .Notes("RequestDeviceAsync shows the browser device chooser and returns an IUsbDevice (null if the "
                + "user dismisses it). Transfer payloads cross as byte[]; dispose the device to release it. "
                + "Actual transfers are device-specific, so the demo shows discovery + lifecycle. Gate on "
                + "IsSupportedAsync.")
            .Result(UsbDemo)
    ];
}
