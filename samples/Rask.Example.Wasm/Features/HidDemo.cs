using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IHid" /> — pair with a HID device from a gesture, open it, and watch its input-report stream
///     live. WASM-only: requestDevice() needs a live user gesture and the live device handle, and it's
///     Chromium-family only at the time of writing. Move/press the device after "Watch" to see reports arrive.
/// </summary>
public sealed partial class HidDemo(IHid hid) : Component, IAsyncDisposable
{
    private IHidDevice? _device;
    private HidDeviceInfo? _info;
    private IAsyncDisposable? _watch;
    private int _reportCount;
    private string _lastReport = "—";
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "hid-request", OnClickAsync: RequestDevice)[
                        I(Class: "bi bi-controller me-1"), "Pair device"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "hid-watch",
                        Disabled: _device is null || _watch is not null, OnClickAsync: Watch)["Open & watch"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "hid-close", Disabled: _device is null,
                        OnClickAsync: Release)["Release"]
                ],
                _info is null
                    ? Div(Class: "small text-secondary")["No device paired."]
                    : Dl(Class: "row small mb-2", Id: "hid-info")[
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Vendor ID"],
                        Dd(Class: "col-7 col-sm-8")[Code()["0x" + _info.VendorId.ToString("x4")]],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Product ID"],
                        Dd(Class: "col-7 col-sm-8")[Code()["0x" + _info.ProductId.ToString("x4")]],
                        Dt(Class: "col-5 col-sm-4 text-secondary")["Product"],
                        Dd(Class: "col-7 col-sm-8")[_info.ProductName ?? "—"]
                    ],
                Div(Class: "small text-secondary")["Reports: ", Code(Id: "hid-count")[_reportCount.ToString()]],
                Div(Class: "small text-secondary")["Last: ", Code(Id: "hid-last")[_lastReport]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "hid-status")[_status]]
            ]
        ];

    private async Task RequestDevice()
    {
        try
        {
            if (!await hid.IsSupportedAsync())
            {
                _status = "WebHID not supported in this browser (Chromium-family only)";
                return;
            }

            await CloseInternal();
            var devices = await hid.RequestDevicesAsync(); // no filters → offer all devices
            _device = devices.Count > 0 ? devices[0] : null;
            _info = _device?.Info;
            _status = _device is null ? "No device selected" : "Paired — click Open & watch";
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Watch()
    {
        if (_device is null || _watch is not null)
        {
            return;
        }

        try
        {
            await _device.OpenAsync();
            _watch = await _device.WatchInputReportsAsync(OnReport, OnDisconnect);
            _status = "Watching — interact with the device";
        }
        catch (Exception ex)
        {
            _status = "Open/watch failed: " + ex.Message;
        }
    }

    private Task OnReport(HidInputReport report)
    {
        // Input reports can arrive very fast (a gamepad fires ~60–125/s); coalesce so we don't re-render the
        // whole card per report — track every one, but repaint at most every 4th.
        _reportCount++;
        _lastReport = $"#{report.ReportId} [{Convert.ToHexString(report.Data)}]";
        if (_reportCount % 4 == 0)
        {
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnect()
    {
        _device = null;
        _info = null;
        _watch = null;
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
        if (_watch is not null)
        {
            await _watch.DisposeAsync();
            _watch = null;
        }

        if (_device is not null)
        {
            await _device.DisposeAsync();
            _device = null;
            _info = null;
        }
    }

    public async ValueTask DisposeAsync() => await CloseInternal();
}
