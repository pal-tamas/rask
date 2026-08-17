using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="UsbDemo" /> (<c>IUsb</c>). Surfaced in the shared sidebar via a
///     host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
public sealed partial class UsbPage : Page
{
    protected override string Route => "usb";

    protected override Type? Parent => typeof(ShowcaseLayout);

    protected override Component? HeadAssets => Title["WebUSB — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["WebUSB"],
        P.Class("text-secondary")[
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
