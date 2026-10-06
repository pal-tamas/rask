using System.Globalization;

namespace Rask.Site.Features;

/// <summary>
///     MDN's WebUSB API from Rask.Web — pair with a USB device from a gesture and read its descriptor (vendor /
///     product / manufacturer / serial), then open and release it. WASM-only: requestDevice() needs a live user
///     gesture, and it's Chromium-family only at the time of writing. Actual data transfer (claim an interface,
///     transferIn/Out) is device-specific, so this demo shows discovery + lifecycle.
/// </summary>
public sealed partial class UsbDemo : Component
{
    private Types.USBDevice? _device;
    private IAsyncDisposable? _unplugged;
    private UsbInfo? _info;
    private bool _open;
    private string _status = "(idle)";

    private sealed record UsbInfo(int VendorId, int ProductId, string? ManufacturerName, string? ProductName, string? SerialNumber);

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("usb-request").OnClick(RequestDevice)[Ui.Icon.Name(Ui.IconName.Cube), "Pair device"],
                    Ui.Button.Primary.Outline
                        .Id("usb-open")
                        .Disabled(_device is null || _open)
                        .OnClick(Open)["Open"],
                    Ui.Button.Error.Outline
                        .Id("usb-close")
                        .Disabled(_device is null)
                        .OnClick(Release)["Release"]
                ],
                _info is null
                    ? Div.Class("text-sm text-ui-muted")["No device paired."]
                    : Dl.Class("grid grid-cols-12 gap-4 text-sm mb-2").Id("usb-info")[
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Vendor ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code[Hex(_info.VendorId)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Product ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code[Hex(_info.ProductId)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Manufacturer"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.ManufacturerName ?? "—"],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Product"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.ProductName ?? "—"],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Serial"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.SerialNumber ?? "—"]
                    ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("usb-status")[_status]]
            ];

    private static string Hex(int value) => "0x" + value.ToString("x4", CultureInfo.InvariantCulture);

    // navigator.usb.requestDevice({ filters: [] }) offers every device; dismissing the chooser rejects.
    private async Task RequestDevice()
    {
        try
        {
            if (!await Navigator.Usb.IsSupported)
            {
                _status = "WebUSB not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            _device = await Navigator.Usb.RequestDevice(new() { Filters = [] });
            _info = await Describe(_device);
            _unplugged ??= await Navigator.Usb.OnDisconnect(Unplugged);
            _status = "Paired";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private static async Task<UsbInfo> Describe(Types.USBDevice device) =>
        new(await device.VendorId, await device.ProductId, await device.ManufacturerName, await device.ProductName,
            await device.SerialNumber);

    private async Task Open()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            await _device.Open();
            _open = true;
            _status = "Opened — ready for device-specific transfers";
        }
        catch (Exception ex)
        {
            _status = "Open failed: " + ex.Message;
        }
    }

    // navigator.usb's `disconnect` fires for any device, so ask getDevices() whether the paired one is still there.
    private async Task Unplugged()
    {
        if (_info is null)
        {
            return;
        }

        var present = false;
        foreach (var device in await Navigator.Usb.GetDevices())
        {
            await using (device)
            {
                present |= await Describe(device) == _info;
            }
        }

        if (!present)
        {
            _open = false; // an unplugged device is closed already
            await CloseInternal();
            _status = "Device disconnected";
        }
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
            if (_open)
            {
                await _device.Close();
            }

            await _device.DisposeAsync();
            _device = null;
            _info = null;
            _open = false;
        }
    }

    protected override async Task OnUnmount()
    {
        await CloseInternal();
        if (_unplugged is not null)
        {
            await _unplugged.DisposeAsync();
        }
    }
}
