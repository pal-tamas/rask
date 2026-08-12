using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="HidDemo" /> (<c>IHid</c>). Surfaced in the shared sidebar via a
///     host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("hid")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class HidPage : Component
{
    protected override Component? HeadAssets => Title["WebHID — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["WebHID"],
        P.Class("text-secondary")[
            "Talk to a human-interface device that no higher-level API covers — a gamepad with custom reports, ",
            "a keyboard with extra keys, simulation controls, point-of-sale hardware — via IHid (the WebHID ",
            "API): open it, send output / feature reports, and subscribe to its live input-report stream. ",
            "WASM-only: requestDevice() needs a live user gesture and the live device handle, and it's ",
            "Chromium-family only at the time of writing."
        ],
        CodeSample
            .Files(["HidDemo.cs"])
            .Notes("RequestDevicesAsync shows the browser chooser and returns the granted devices (empty if "
                + "dismissed). Open a device, then WatchInputReportsAsync pushes each input report (and an "
                + "optional disconnect signal) to your callback; dispose the watch and the device to release. "
                + "Report payloads cross as byte[]. Gate on IsSupportedAsync.")
            .Result(HidDemo)
    ];
}
