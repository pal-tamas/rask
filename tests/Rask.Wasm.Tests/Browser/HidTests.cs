using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class HidTests
{
    private static HidDeviceInfo SampleInfo => new(0x046d, 0xc52b, "Acme Controller");

    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.isSupported", true);

        Assert.True(await new Hid(js).IsSupportedAsync());
    }

    [Fact]
    public async Task RequestDevices_PassesFiltersAsOneArg_AndWrapsDevices()
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
    public async Task RequestDevices_Cancelled_ReturnsEmpty()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", Array.Empty<HidDeviceHandshake>());

        var devices = await new Hid(js).RequestDevicesAsync();

        Assert.Empty(devices);
    }

    [Fact]
    public async Task SendReport_And_SendFeatureReport_EncodeBase64()
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
    public async Task ReceiveFeatureReport_DecodesBase64()
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
    public async Task Watch_RoutesInputReports_DecodingBase64()
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
    public async Task MultipleWatches_BothReceiveReports_AndDisposingOneKeepsTheOther()
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
    public async Task WatchDispose_Unwatches_AndStopsRouting()
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
    public async Task Disconnect_FiresOnDisconnect_WhenWatching()
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
    public async Task Close_StopsRouting()
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
    public async Task Operations_AfterDispose_ThrowObjectDisposed()
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
    public async Task NullArgs_Throw()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskHid.requestDevices", new[] { new HidDeviceHandshake(1, SampleInfo) });
        var device = (await new Hid(js).RequestDevicesAsync())[0];

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await device.SendReportAsync(1, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await device.WatchInputReportsAsync(null!));
    }

    [Fact]
    public void VendorOnlyFilter_OmitsNullFields()
    {
        var json = JsonSerializer.Serialize(
            new HidDeviceFilter(VendorId: 0x046d), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("vendorId", json);
        Assert.DoesNotContain("productId", json);
        Assert.DoesNotContain("usagePage", json);
    }
}
