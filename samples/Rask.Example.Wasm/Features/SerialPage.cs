using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="SerialDemo" /> (<c>ISerial</c>). Surfaced in the shared sidebar
///     via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("serial")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class SerialPage : Component
{
    protected override Component? Head => Title()["Web Serial — Rask"];

    protected override Component? Render() =>
    [
        H1(Class: "h2 mb-1")["Web Serial"],
        P(Class: "text-secondary")[
            "Talk to a serial device — an Arduino or microcontroller, a GPS, a USB-to-serial adapter — ",
            "straight from C# via ISerial (the Web Serial API): pick a port, write a line, and watch inbound ",
            "bytes stream into the log. WASM-only: requestPort() needs a live user gesture and the live port ",
            "stream, and it's Chromium-family only at the time of writing."
        ],
        CodeSample(
            ["SerialDemo.cs"],
            Notes: "RequestPortAsync shows the browser port chooser, opens the port, and starts a read loop "
                + "that pushes inbound bytes to your callback; it returns null if the user dismisses the "
                + "chooser (not an error). Dispose the port to stop reading and release it. Gate on "
                + "IsSupportedAsync.",
            Result: SerialDemo())
    ];
}
