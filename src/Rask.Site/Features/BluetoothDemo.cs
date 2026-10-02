using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's Web Bluetooth API from Rask.Web — pair with a Bluetooth Low Energy device, connect to its GATT server,
///     and read the standard Battery Service's <c>battery_level</c> (0–100%). WASM-only: requestDevice() needs a live
///     user gesture, and it's Chromium-family only at the time of writing.
/// </summary>
public sealed partial class BluetoothDemo : Component
{
    private Types.BluetoothDevice? _device;
    private IAsyncDisposable? _disconnectWatch;
    private string? _name;
    private string _battery = "—";
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("bt-request").OnClick(PairAndRead)[Ui.Icon.Name(Ui.IconName.Signal), "Pair & read battery"],
                    Ui.Button.Error.Outline
                        .Id("bt-disconnect")
                        .Disabled(_device is null)
                        .OnClick(Disconnect)["Disconnect"]
                ],
                _name is null
                    ? Div.Class("text-sm text-ui-muted")["No device paired."]
                    : Dl.Class("grid grid-cols-12 gap-4 text-sm mb-2").Id("bt-info")[
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Device"],
                        Dd.Class("col-span-7 sm:col-span-8")[_name],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Battery"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code.Id("bt-battery")[_battery]]
                    ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("bt-status")[_status]]
            ];

    // navigator.bluetooth.requestDevice() shows the chooser (dismissing it rejects), then the GATT server walks
    // service → characteristic → readValue(), whose DataView comes back as bytes.
    private async Task PairAndRead()
    {
        try
        {
            if (!await Navigator.Bluetooth.IsSupported)
            {
                _status = "Web Bluetooth not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            _device = await Navigator.Bluetooth.RequestDevice(new()
            {
                Filters = [new() { Services = ["battery_service"] }],
                OptionalServices = ["battery_service"]
            });
            _name = await _device.Name ?? await _device.Id;
            _disconnectWatch = await _device.OnGattServerDisconnected(async () =>
            {
                await CloseInternal();
                _status = "Device disconnected";
            });

            await using var server = await _device.Gatt.Connect();
            await using var battery = await server.GetPrimaryService("battery_service");
            await using var level = await battery.GetCharacteristic("battery_level");
            var bytes = await level.ReadValue();
            _battery = bytes.Length > 0 ? $"{bytes[0]}%" : "(empty)";
            _status = "Connected — battery read";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    // Stop listening first, so dropping the link ourselves isn't reported as the device going away.
    private async Task Disconnect()
    {
        await StopWatching();
        if (_device is not null)
        {
            await _device.Gatt.Disconnect();
        }

        await CloseInternal();
        _status = "Disconnected";
    }

    private async Task StopWatching()
    {
        if (_disconnectWatch is not null)
        {
            await _disconnectWatch.DisposeAsync();
            _disconnectWatch = null;
        }
    }

    private async Task CloseInternal()
    {
        await StopWatching();

        if (_device is not null)
        {
            await _device.DisposeAsync();
            _device = null;
            _name = null;
            _battery = "—";
        }
    }

    protected override Task OnUnmount() => CloseInternal();
}
