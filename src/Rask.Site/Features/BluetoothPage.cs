using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     WASM-only showcase page for <see cref="BluetoothDemo" /> (<c>IBluetooth</c>). Surfaced in the shared
///     sidebar via a host-registered <see cref="ShowcaseNavEntry" /> (see Program.cs).
/// </summary>
[Route("bluetooth")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class BluetoothPage : Component
{
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Web Bluetooth — Rask",
            "Pair with a Bluetooth Low Energy device and talk to its GATT services from C# — connect, read and write characteristics, and subscribe to notifications — through Rask's IBluetooth wrapper over the Web Bluetooth API.",
            Routes.BluetoothPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Web Bluetooth"],
        P.Class("text-ui-muted")[
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
