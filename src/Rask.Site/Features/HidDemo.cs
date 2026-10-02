using System.Globalization;
using Rask.Web;

namespace Rask.Site.Features;

/// <summary>
///     MDN's WebHID API from Rask.Web — pair with a HID device from a gesture, open it, and watch its input-report
///     stream live. WASM-only: requestDevice() needs a live user gesture, and it's Chromium-family only at the time
///     of writing. Move/press the device after "Watch" to see reports arrive.
/// </summary>
public sealed partial class HidDemo : Component
{
    private Rask.Web.Types.HIDDevice? _device;
    private HidInfo? _info;
    private IAsyncDisposable? _watch;
    private IAsyncDisposable? _unplugged;
    private int _reportCount;
    private string _lastReport = "—";
    private string _status = "(idle)";

    private sealed record HidInfo(int VendorId, int ProductId, string ProductName);

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("hid-request").OnClick(RequestDevice)[Ui.Icon.Name(Ui.IconName.Cube), "Pair device"],
                    Ui.Button.Primary.Outline
                        .Id("hid-watch")
                        .Disabled(_device is null || _watch is not null)
                        .OnClick(Watch)["Open & watch"],
                    Ui.Button.Error.Outline
                        .Id("hid-close")
                        .Disabled(_device is null)
                        .OnClick(Release)["Release"]
                ],
                _info is null
                    ? Div.Class("text-sm text-ui-muted")["No device paired."]
                    : Dl.Class("grid grid-cols-12 gap-4 text-sm mb-2").Id("hid-info")[
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Vendor ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code["0x" + _info.VendorId.ToString("x4", CultureInfo.InvariantCulture)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Product ID"],
                        Dd.Class("col-span-7 sm:col-span-8")[Code["0x" + _info.ProductId.ToString("x4", CultureInfo.InvariantCulture)]],
                        Dt.Class("col-span-5 sm:col-span-4 text-ui-muted")["Product"],
                        Dd.Class("col-span-7 sm:col-span-8")[_info.ProductName]
                    ],
                Div.Class("text-sm text-ui-muted")["Reports: ", Code.Id("hid-count")[_reportCount]],
                Div.Class("text-sm text-ui-muted")["Last: ", Code.Id("hid-last")[_lastReport]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("hid-status")[_status]]
            ];

    // navigator.hid.requestDevice({ filters: [] }) offers every device and answers the ones granted (none if dismissed).
    private async Task RequestDevice()
    {
        try
        {
            if (!await Navigator.Hid.IsSupported)
            {
                _status = "WebHID not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            var granted = await Navigator.Hid.RequestDevice(new() { Filters = [] });
            foreach (var extra in granted.Skip(1))
            {
                await extra.DisposeAsync();
            }

            _device = granted.FirstOrDefault();
            _info = _device is null ? null : await Describe(_device);
            _unplugged ??= await Navigator.Hid.OnDisconnect(Unplugged);
            _status = _device is null ? "No device selected" : "Paired — click Open & watch";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private static async Task<HidInfo> Describe(Rask.Web.Types.HIDDevice device) =>
        new(await device.VendorId, await device.ProductId, await device.ProductName);

    // Every `inputreport` event re-renders this component with the report's id and bytes.
    private async Task Watch()
    {
        if (_device is null || _watch is not null)
        {
            return;
        }

        try
        {
            await _device.Open();
            _watch = await _device.OnInputReport(report =>
            {
                _reportCount++;
                _lastReport = $"#{report.ReportId} [{Convert.ToHexString(report.Data)}]";
            });
            _status = "Watching — interact with the device";
        }
        catch (Exception ex)
        {
            _status = "Open/watch failed: " + ex.Message;
        }
    }

    // navigator.hid's `disconnect` fires for any device, so ask getDevices() whether the paired one is still there.
    private async Task Unplugged()
    {
        if (_info is null)
        {
            return;
        }

        var present = false;
        foreach (var device in await Navigator.Hid.GetDevices())
        {
            await using (device)
            {
                present |= await Describe(device) == _info;
            }
        }

        if (!present)
        {
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
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
            _watch = null;
        }

        if (_device is not null)
        {
            if (await _device.Opened)
            {
                await _device.Close();
            }

            await _device.DisposeAsync();
            _device = null;
            _info = null;
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
