using Rask.Example.Shared;
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
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Ui.BtnPrimary).Id("usb-request").OnClickAsync(RequestDevice)[
                        Icon.Name(IconName.UsbDrive).Class("me-1"), "Pair device"],
                    Button
                        .Class(Ui.BtnOutlinePrimary)
                        .Id("usb-open")
                        .Disabled(_device is null || _open)
                        .OnClickAsync(Open)["Open"],
                    Button
                        .Class(Ui.BtnOutlineDanger)
                        .Id("usb-close")
                        .Disabled(_device is null)
                        .OnClickAsync(Release)["Release"]
                ],
                _info is null
                    ? Div.Class("text-sm text-slate-500 dark:text-slate-400")["No device paired."]
                    : Dl.Class("grid grid-cols-12 gap-4 text-sm mb-2").Id("usb-info")[
                        Dt.Class("col-span-5 sm:col-span-4 text-slate-500 dark:text-slate-400")["Vendor ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code[Hex(_info.VendorId)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-slate-500 dark:text-slate-400")["Product ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code[Hex(_info.ProductId)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-slate-500 dark:text-slate-400")["Manufacturer"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.ManufacturerName ?? "—"],
                        Dt.Class("col-span-5 sm:col-span-4 text-slate-500 dark:text-slate-400")["Product"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.ProductName ?? "—"],
                        Dt.Class("col-span-5 sm:col-span-4 text-slate-500 dark:text-slate-400")["Serial"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.SerialNumber ?? "—"]
                    ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("usb-status")[_status]]
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
