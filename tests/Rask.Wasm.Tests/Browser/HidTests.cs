using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class HidTests
{
    private static HidDeviceInfo SampleInfo => new(0x046d, 0xc52b, "Acme Controller");

    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.isSupported", true);

        Assert.True(await new Hid(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Requesting_devices_passes_the_filters_as_one_argument_and_wraps_the_devices()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(1, SampleInfo) });
        var filters = new[] { new HidDeviceFilter(VendorId: 0x046d) };

        var devices = await new Hid(js).RequestDevicesAsync(filters);

        Assert.Single(devices);
        Assert.Equal(SampleInfo, devices[0].Info);
        var args = js.ArgsFor("__raskHid.requestDevices");
        Assert.Single(args!);
        Assert.Same(filters, args![0]);
    }

    [Fact]
    public async Task A_cancelled_devices_request_returns_empty()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", Array.Empty<HidDeviceHandshake>());

        var devices = await new Hid(js).RequestDevicesAsync();

        Assert.Empty(devices);
    }

    [Fact]
    public async Task Sending_a_report_and_a_feature_report_encodes_base64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(4, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        var data = new byte[] { 1, 2, 3 };

        await device.SendReportAsync(reportId: 2, data);
        await device.SendFeatureReportAsync(reportId: 5, data);

        Assert.Equal([4, 2, Convert.ToBase64String(data)], js.ArgsFor("__raskHid.sendReport"));
        Assert.Equal([4, 5, Convert.ToBase64String(data)], js.ArgsFor("__raskHid.sendFeatureReport"));
    }

    [Fact]
    public async Task Receiving_a_feature_report_decodes_base64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(1, SampleInfo) });
        var bytes = new byte[] { 7, 8, 9 };
        js.SetResponse("__raskHid.receiveFeatureReport", Convert.ToBase64String(bytes));
        var device = (await new Hid(js).RequestDevicesAsync())[0];

        var got = await device.ReceiveFeatureReportAsync(reportId: 3);

        Assert.Equal(bytes, got);
        Assert.Equal([1, 3], js.ArgsFor("__raskHid.receiveFeatureReport"));
    }

    [Fact]
    public async Task A_watch_routes_input_reports_decoding_base64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(6, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        HidInputReport? got = null;
        await device.WatchInputReportsAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var payload = new byte[] { 10, 20 };

        await HidInterop.Input(6, reportId: 1, Convert.ToBase64String(payload));

        Assert.NotNull(got);
        Assert.Equal(1, got!.ReportId);
        Assert.Equal(payload, got.Data);
        Assert.Equal([6], js.ArgsFor("__raskHid.watch"));
    }

    [Fact]
    public async Task Several_watches_all_receive_reports_and_disposing_one_keeps_the_others()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(6, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        var a = 0;
        var b = 0;
        var watchA = await device.WatchInputReportsAsync(_ => { a++; return Task.CompletedTask; });
        await device.WatchInputReportsAsync(_ => { b++; return Task.CompletedTask; });

        await HidInterop.Input(6, 1, Convert.ToBase64String([1])); // both fire

        Assert.Equal(1, a);
        Assert.Equal(1, b);

        await watchA.DisposeAsync();

        await HidInterop.Input(6, 1, Convert.ToBase64String([2])); // only B survives

        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public async Task Disposing_a_watch_unwatches_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(6, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        var fired = 0;
        var watch = await device.WatchInputReportsAsync(_ =>
        {
            fired++;
            return Task.CompletedTask;
        });

        await watch.DisposeAsync();

        Assert.Equal([6], js.ArgsFor("__raskHid.unwatch"));

        await HidInterop.Input(6, 1, Convert.ToBase64String([0]));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task A_disconnect_fires_the_callback_while_watching()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(8, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        var disconnected = 0;
        await device.WatchInputReportsAsync(
            _ => Task.CompletedTask,
            () => { disconnected++; return Task.CompletedTask; });

        await HidInterop.Disconnected(8);

        Assert.Equal(1, disconnected);
    }

    [Fact]
    public async Task Closing_stops_routing()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(2, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        var fired = 0;
        await device.WatchInputReportsAsync(_ =>
        {
            fired++;
            return Task.CompletedTask;
        });

        await device.DisposeAsync();

        Assert.Equal([2], js.ArgsFor("__raskHid.close"));

        await HidInterop.Input(2, 1, Convert.ToBase64String([0]));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Operations_after_dispose_throw_ObjectDisposedException()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(1, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        await device.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await device.OpenAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await device.SendReportAsync(1, [0]));
    }

    [Fact]
    public async Task The_input_fan_out_gives_each_watcher_its_own_copy_and_isolates_exceptions()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(6, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];
        byte[]? survivor = null;
        // First watcher mutates its copy and throws — must not corrupt or starve the second.
        await device.WatchInputReportsAsync(r =>
        {
            r.Data[0] = 0xFF;
            throw new InvalidOperationException("boom");
        });
        await device.WatchInputReportsAsync(r =>
        {
            survivor = r.Data;
            return Task.CompletedTask;
        });

        await HidInterop.Input(6, 1, Convert.ToBase64String([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 2, 3 }, survivor); // own copy, untouched by the other's mutation/throw
    }

    [Fact]
    public async Task Null_arguments_throw()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(1, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await device.SendReportAsync(1, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await device.WatchInputReportsAsync(null!));
    }

    [Fact]
    public void A_vendor_only_filter_omits_null_fields()
    {
        var json = JsonSerializer.Serialize(
            new HidDeviceFilter(VendorId: 0x046d), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("vendorId", json);
        Assert.DoesNotContain("productId", json);
        Assert.DoesNotContain("usagePage", json);
    }
}
