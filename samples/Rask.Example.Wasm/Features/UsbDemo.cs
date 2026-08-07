using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IUsb" /> — pair with a USB device from a gesture and read its descriptor (vendor / product /
///     manufacturer / serial), then open and release it. WASM-only: requestDevice() needs a live user gesture
///     and the live device handle, and it's Chromium-family only at the time of writing. Actual data transfer
///     (claim an interface, transferIn/Out) is device-specific, so this demo shows discovery + lifecycle.
/// </summary>
public sealed partial class UsbDemo(IUsb usb) : Component, IAsyncDisposable
{
    private IUsbDevice? _device;
    private UsbDeviceInfo? _info;
    private bool _open;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "usb-request", OnClickAsync: RequestDevice)[
                        I(Class: "bi bi-usb-drive me-1"), "Pair device"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "usb-open", Disabled: _device is null || _open,
                        OnClickAsync: Open)["Open"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "usb-close", Disabled: _device is null,
                        OnClickAsync: Release)["Release"]
                ],
                _info is null
                    ? Div(Class: "small text-secondary")["No device paired."]
                    : Dl(Class: "row small mb-2", Id: "usb-info")[
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Vendor ID"],
                        Dd(Class: "col-7 col-sm-8")[Code()[Hex(_info.VendorId)]],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Product ID"],
                        Dd(Class: "col-7 col-sm-8")[Code()[Hex(_info.ProductId)]],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Manufacturer"],
                        Dd(Class: "col-7 col-sm-8")[_info.ManufacturerName ?? "—"],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Product"],
                        Dd(Class: "col-7 col-sm-8")[_info.ProductName ?? "—"],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Serial"],
                        Dd(Class: "col-7 col-sm-8")[_info.SerialNumber ?? "—"]
                    ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "usb-status")[_status]]
            ]
        ];

    private static string Hex(int value) => "0x" + value.ToString("x4");

    private async Task RequestDevice()
    {
        try
        {
            if (!await usb.IsSupportedAsync())
            {
                _status = "WebUSB not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            _device = await usb.RequestDeviceAsync(onDisconnect: OnDisconnect); // no filters → offer all devices
            _info = _device?.Info;
            _status = _device is null ? "No device selected" : "Paired";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Open()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            await _device.OpenAsync();
            _open = true;
            _status = "Opened — ready for device-specific transfers";
        }
        catch (Exception ex)
        {
            _status = "Open failed: " + ex.Message;
        }
    }

    private Task OnDisconnect()
    {
        // The device was unplugged — the framework already evicted it; just reset the UI.
        _device = null;
        _info = null;
        _open = false;
        _status = "Device disconnected";
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task Release()
    {
        await CloseInternal();
        _status = "Released — device closed";
    }

    private async Task CloseInternal()
    {
        if (_device is not null)
        {
            await _device.DisposeAsync();
            _device = null;
            _info = null;
            _open = false;
        }
    }

    public async ValueTask DisposeAsync() => await CloseInternal();
}
