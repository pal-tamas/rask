using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     WASM-only showcase page for <see cref="SerialDemo" /> (<c>ISerial</c>). Surfaced in the shared sidebar
///     via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("serial")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class SerialPage : Component
{
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Web Serial — Rask",
            "Talk to an Arduino, a GPS or a USB-to-serial adapter straight from C# — pick a port, write a line, stream inbound bytes — through Rask's ISerial wrapper over the Web Serial API.",
            Routes.SerialPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Web Serial"],
        P.Class("text-ui-muted")[
            "Talk to a serial device — an Arduino or microcontroller, a GPS, a USB-to-serial adapter — ",
            "straight from C# via ISerial (the Web Serial API): pick a port, write a line, and watch inbound ",
            "bytes stream into the log. WASM-only: requestPort() needs a live user gesture and the live port ",
            "stream, and it's Chromium-family only at the time of writing."
        ],
        CodeSample
            .Files(["SerialDemo.cs"])
            .Notes("RequestPortAsync shows the browser port chooser, opens the port, and starts a read loop "
                + "that pushes inbound bytes to your callback; it returns null if the user dismisses the "
                + "chooser (not an error). Dispose the port to stop reading and release it. Gate on "
                + "IsSupportedAsync.")
            .Result(SerialDemo)
    ];
}
