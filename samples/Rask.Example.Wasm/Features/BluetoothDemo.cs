using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IBluetooth" /> — pair with a Bluetooth Low Energy device that advertises the standard
///     Battery Service, connect to its GATT server, and read the battery level. WASM-only: requestDevice()
///     needs a live user gesture and the live device handle, and it's Chromium-family only at the time of
///     writing. Reads the <c>battery_level</c> characteristic (0–100%).
/// </summary>
public sealed partial class BluetoothDemo(IBluetooth bluetooth) : Component, IAsyncDisposable
{
    private IBluetoothDevice? _device;
    private IAsyncDisposable? _disconnectWatch;
    private string? _name;
    private string _battery = "—";
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Tw.BtnPrimary).Id("bt-request").OnClickAsync(PairAndRead)[
                        Icon.Name(IconName.Bluetooth).Class("me-1"), "Pair & read battery"],
                    Button
                        .Class(Tw.BtnOutlineDanger)
                        .Id("bt-disconnect")
                        .Disabled(_device is null)
                        .OnClickAsync(Disconnect)["Disconnect"]
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
            ]
        ];

    private async Task PairAndRead()
    {
        try
        {
            if (!await bluetooth.IsSupportedAsync())
            {
                _status = "Web Bluetooth not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            _device = await bluetooth.RequestDeviceAsync(new BluetoothRequestOptions(
                Filters: [new BluetoothFilter(Services: ["battery_service"])]));
            if (_device is null)
            {
                _status = "No device selected";
                return;
            }

            _name = _device.Info.Name ?? _device.Info.Id;
            _disconnectWatch = await _device.WatchDisconnectAsync(OnDisconnect);

            await _device.ConnectAsync();
            var level = await _device.GetCharacteristicAsync("battery_service", "battery_level");
            var bytes = await level.ReadAsync();
            _battery = bytes.Length > 0 ? $"{bytes[0]}%" : "(empty)";
            _status = "Connected — battery read";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task OnDisconnect()
    {
        // The device dropped its GATT link — release the handle (and its watch) so nothing leaks, then reset.
        await CloseInternal();
        _status = "Device disconnected";
        StateHasChanged();
    }

    private async Task Disconnect()
    {
        await CloseInternal();
        _status = "Disconnected";
    }

    private async Task CloseInternal()
    {
        if (_disconnectWatch is not null)
        {
            await _disconnectWatch.DisposeAsync();
            _disconnectWatch = null;
        }

        if (_device is not null)
        {
            await _device.DisposeAsync();
            _device = null;
            _name = null;
            _battery = "—";
        }
    }

    public async ValueTask DisposeAsync() => await CloseInternal();
}
