using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="DeviceSensorsDemo" /> (<c>IDeviceOrientation</c> + <c>IDeviceMotion</c>).</summary>
[Route("browser/device-sensors")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DeviceSensorsPage : Component
{
    protected override RenderResult Head => Title()["Device sensors — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Device sensors",
            "Read the gyroscope/compass (IDeviceOrientation) and accelerometer (IDeviceMotion) — for "
            + "tilt-controlled UIs, AR overlays, a compass, or shake gestures. iOS requires a permission "
            + "grant from a user gesture. Each reading is pushed from JS via a static [JSInvokable], so one "
            + "wiring serves both Server and WASM."),
        CodeSample(
            ["DeviceSensorsDemo.cs"],
            Notes: "WatchAsync(handler) returns a disposable subscription; readings only flow on a device "
                + "with motion hardware, so the readout stays '—' on a desktop without sensors.",
            Result: DeviceSensorsDemo())
    ];
}
