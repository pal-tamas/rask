using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class BluetoothTests
{
    private static BluetoothDeviceInfo SampleInfo => new("dev-abc", "Heart Monitor");

    private static async Task<IBluetoothDevice> RequestDeviceAsync(FakeJsRuntime js, int id = 1)
    {
        js.SetResponse("__raskBluetooth.requestDevice", new BluetoothDeviceHandshake(id, SampleInfo));
        return (await new Bluetooth(js).RequestDeviceAsync(new BluetoothRequestOptions(AcceptAllDevices: true)))!;
    }

    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.isSupported", true);

        Assert.True(await new Bluetooth(js).IsSupportedAsync());
    }

    [Fact]
    public async Task RequestDevice_PassesOptions_AndReturnsDevice()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.requestDevice", new BluetoothDeviceHandshake(1, SampleInfo));

        var options = new BluetoothRequestOptions(
            Filters: [new BluetoothFilter(Services: ["battery_service"])],
            OptionalServices: ["device_information"]);
        var device = await new Bluetooth(js).RequestDeviceAsync(options);

        Assert.NotNull(device);
        Assert.Equal(SampleInfo, device!.Info);
        Assert.Same(options, js.ArgsFor("__raskBluetooth.requestDevice")![0]);
    }

    [Fact]
    public async Task RequestDevice_Cancelled_ReturnsNull()
    {
        var js = new FakeJsRuntime(); // no canned response → JS returned null (chooser dismissed)

        var device = await new Bluetooth(js).RequestDeviceAsync(new BluetoothRequestOptions(AcceptAllDevices: true));

        Assert.Null(device);
    }

    [Fact]
    public async Task RequestDevice_NullOptions_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new Bluetooth(new FakeJsRuntime()).RequestDeviceAsync(null!));
    }

    [Fact]
    public async Task RequestDevice_NoFiltersNoAcceptAll_Throws()
    {
        // Web Bluetooth requires filters or acceptAllDevices — a default options object is invalid.
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await new Bluetooth(new FakeJsRuntime()).RequestDeviceAsync(new BluetoothRequestOptions()));
    }

    [Fact]
    public async Task Connect_Disconnect_IsConnected_ForwardId()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.isConnected", true);
        var device = await RequestDeviceAsync(js, 7);

        await device.ConnectAsync();
        var connected = await device.IsConnectedAsync();
        await device.DisconnectAsync();

        Assert.True(connected);
        Assert.Equal([7], js.ArgsFor("__raskBluetooth.connect"));
        Assert.Equal([7], js.ArgsFor("__raskBluetooth.isConnected"));
        Assert.Equal([7], js.ArgsFor("__raskBluetooth.disconnect"));
    }

    [Fact]
    public async Task GetCharacteristic_PassesUuids_AndReadDecodesBase64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 42);
        var bytes = new byte[] { 99 };
        js.SetResponse("__raskBluetooth.readValue", Convert.ToBase64String(bytes));
        var device = await RequestDeviceAsync(js, 3);

        var ch = await device.GetCharacteristicAsync("battery_service", "battery_level");
        var value = await ch.ReadAsync();

        Assert.Equal(bytes, value);
        Assert.Equal([3, "battery_service", "battery_level"], js.ArgsFor("__raskBluetooth.getCharacteristic"));
        Assert.Equal([42], js.ArgsFor("__raskBluetooth.readValue"));
    }

    [Fact]
    public async Task Write_EncodesBase64_AndPassesWithResponse()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 5);
        var device = await RequestDeviceAsync(js);
        var ch = await device.GetCharacteristicAsync("svc", "chr");

        var data = new byte[] { 1, 2, 3 };
        await ch.WriteAsync(data, withResponse: false);

        var args = js.ArgsFor("__raskBluetooth.writeValue");
        Assert.Equal(5, args![0]);
        Assert.Equal(Convert.ToBase64String(data), args[1]);
        Assert.Equal(false, args[2]);
    }

    [Fact]
    public async Task Notifications_FanOutToWatchers_AndDisposeStopsOne()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 8);
        var device = await RequestDeviceAsync(js);
        var ch = await device.GetCharacteristicAsync("svc", "chr");
        var a = 0;
        var b = 0;
        var watchA = await ch.WatchAsync(_ => { a++; return Task.CompletedTask; });
        await ch.WatchAsync(_ => { b++; return Task.CompletedTask; });

        await BluetoothInterop.Value(8, Convert.ToBase64String([7])); // both
        Assert.Equal(1, a);
        Assert.Equal(1, b);

        await watchA.DisposeAsync();
        Assert.Equal([8], js.ArgsFor("__raskBluetooth.stopNotifications"));
        await BluetoothInterop.Value(8, Convert.ToBase64String([7])); // only B
        Assert.Equal(1, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public async Task Notification_DecodesPayload()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 8);
        var device = await RequestDeviceAsync(js);
        var ch = await device.GetCharacteristicAsync("svc", "chr");
        byte[]? got = null;
        await ch.WatchAsync(v => { got = v; return Task.CompletedTask; });

        var payload = new byte[] { 60, 61 };
        await BluetoothInterop.Value(8, Convert.ToBase64String(payload));

        Assert.Equal(payload, got);
        Assert.Equal([8], js.ArgsFor("__raskBluetooth.startNotifications"));
    }

    [Fact]
    public async Task WatchDisconnect_Fires_AndDisposeStops()
    {
        var js = new FakeJsRuntime();
        var device = await RequestDeviceAsync(js, 9);
        var fired = 0;
        var watch = await device.WatchDisconnectAsync(() => { fired++; return Task.CompletedTask; });

        await BluetoothInterop.Disconnected(9);
        Assert.Equal(1, fired);

        await watch.DisposeAsync();
        Assert.Equal([9], js.ArgsFor("__raskBluetooth.unwatchDisconnect"));
        await BluetoothInterop.Disconnected(9);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task GetCharacteristic_SameUuids_ReturnsSameHandle()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 8); // JS dedups to one id per physical char
        var device = await RequestDeviceAsync(js);

        var a = await device.GetCharacteristicAsync("svc", "chr");
        var b = await device.GetCharacteristicAsync("svc", "chr");

        Assert.Same(a, b);
    }

    [Fact]
    public async Task Dispose_ReleasesDevice_Characteristics_AndStopsDisconnectRouting()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 8);
        var device = await RequestDeviceAsync(js, 2);
        var fired = 0;
        await device.WatchDisconnectAsync(() => { fired++; return Task.CompletedTask; });
        await device.GetCharacteristicAsync("svc", "chr"); // resolved char should be released on dispose

        await device.DisposeAsync();
        Assert.Equal([2], js.ArgsFor("__raskBluetooth.release"));      // release (not reusable disconnect)
        Assert.Equal([8], js.ArgsFor("__raskBluetooth.releaseCharacteristic"));

        await BluetoothInterop.Disconnected(2);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task DeviceOperations_AfterDispose_ThrowObjectDisposed()
    {
        var js = new FakeJsRuntime();
        var device = await RequestDeviceAsync(js);
        await device.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await device.ConnectAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await device.GetCharacteristicAsync("svc", "chr"));
    }

    [Fact]
    public async Task NotificationFanout_GivesEachSubscriberOwnCopy_AndIsolatesExceptions()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 8);
        var device = await RequestDeviceAsync(js);
        var ch = await device.GetCharacteristicAsync("svc", "chr");
        byte[]? survivor = null;
        // First subscriber mutates its copy and throws — must not corrupt or starve the second.
        await ch.WatchAsync(v =>
        {
            v[0] = 0xFF;
            throw new InvalidOperationException("boom");
        });
        await ch.WatchAsync(v =>
        {
            survivor = v;
            return Task.CompletedTask;
        });

        await BluetoothInterop.Value(8, Convert.ToBase64String([1, 2, 3]));

        Assert.Equal(new byte[] { 1, 2, 3 }, survivor);
    }

    [Fact]
    public async Task NullArgs_Throw()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBluetooth.getCharacteristic", 1);
        var device = await RequestDeviceAsync(js);
        var ch = await device.GetCharacteristicAsync("svc", "chr");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ch.WriteAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await ch.WatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await device.WatchDisconnectAsync(null!));
    }

    [Fact]
    public void FilterAndOptions_OmitNullFields()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var filter = JsonSerializer.Serialize(new BluetoothFilter(NamePrefix: "HR"), web);
        Assert.Contains("namePrefix", filter);
        Assert.DoesNotContain("services", filter);
        Assert.DoesNotContain("name\"", filter);

        var opts = JsonSerializer.Serialize(new BluetoothRequestOptions(AcceptAllDevices: true), web);
        Assert.Contains("acceptAllDevices", opts);
        Assert.DoesNotContain("filters", opts);
        Assert.DoesNotContain("optionalServices", opts);
    }
}
