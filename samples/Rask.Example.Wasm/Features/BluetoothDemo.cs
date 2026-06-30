using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IBluetooth" /> — pair with a Bluetooth Low Energy device that advertises the standard
///     Battery Service, connect to its GATT server, and read the battery level. WASM-only: requestDevice()
///     needs a live user gesture and the live device handle, and it's Chromium-family only at the time of
///     writing. Reads the <c>battery_level</c> characteristic (0–100%).
/// </summary>
public sealed class BluetoothDemo(IBluetooth bluetooth) : Component, IAsyncDisposable
{
    private IBluetoothDevice? _device;
    private IAsyncDisposable? _disconnectWatch;
    private string? _name;
    private string _battery = "—";
    private string _status = "(idle)";

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "bt-request", OnClickAsync: PairAndRead)[
                        I(Class: "bi bi-bluetooth me-1"), "Pair & read battery"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "bt-disconnect", Disabled: _device is null,
                        OnClickAsync: Disconnect)["Disconnect"]
                ],
                _name is null
                    ? Div(Class: "small text-secondary")["No device paired."]
                    : Dl(Class: "row small mb-2", Id: "bt-info")[
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Device"],
                        Dd(Class: "col-7 col-sm-8")[_name],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Battery"],
                        Dd(Class: "col-7 col-sm-8")[Code(Id: "bt-battery")[_battery]]
                    ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "bt-status")[_status]]
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
