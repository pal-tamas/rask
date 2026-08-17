using Rask.Core.Routing;
using Rask.Example.Shared;
using static Rask.Example.Shared.Generated; // the CodeSample(...) factory (defined in the shared assembly)

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="BluetoothDemo" /> (<c>IBluetooth</c>). Surfaced in the shared
///     sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
public sealed partial class BluetoothPage : Page
{
    protected override string Route => "bluetooth";

    protected override Type? Parent => typeof(ShowcaseLayout);

    protected override Component? HeadAssets => Title["Web Bluetooth — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("h2 mb-1")["Web Bluetooth"],
        P.Class("text-secondary")[
            "Pair with a Bluetooth Low Energy device and talk to its GATT services from C# — connect, read / ",
            "write characteristics, and subscribe to notifications (heart-rate monitors, thermometers, fitness ",
            "sensors, custom hardware) — via IBluetooth (the Web Bluetooth API). WASM-only: requestDevice() ",
            "needs a live user gesture and the live device handle, and it's Chromium-family only at the time of ",
            "writing."
        ],
        CodeSample
            .Files(["BluetoothDemo.cs"])
            .Notes("RequestDeviceAsync shows the chooser and returns an IBluetoothDevice (null if dismissed). "
                + "Connect, then GetCharacteristicAsync(service, characteristic) → read/write/WatchAsync "
                + "(notifications). This demo reads the standard Battery Service. Values cross as byte[]; "
                + "dispose the device to drop the connection. Gate on IsSupportedAsync.")
            .Result(BluetoothDemo)
    ];
}
