using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class SerialTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.isSupported", true);

        Assert.True(await new Serial(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Requesting_a_port_registers_before_opening_and_passes_the_options()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", true);
        var options = new SerialOptions(BaudRate: 115200);

        var port = await new Serial(js).RequestPortAsync(options, _ => Task.CompletedTask);

        Assert.NotNull(port);
        var args = js.ArgsFor("__raskSerial.requestPort");
        Assert.IsType<int>(args![0]);        // C#-minted id
        Assert.Same(options, args[1]);       // options follow the id
    }

    [Fact]
    public async Task A_cancelled_port_request_returns_null_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", false);
        var fired = 0;

        var port = await new Serial(js).RequestPortAsync(new SerialOptions(), _ =>
        {
            fired++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskSerial.requestPort")![0]!;

        Assert.Null(port);

        await SerialInterop.Data(id, Convert.ToBase64String([1])); // handler was unregistered on cancel
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Data_is_routed_to_the_registered_handler()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", true);
        byte[]? got = null;
        await new Serial(js).RequestPortAsync(new SerialOptions(), data =>
        {
            got = data;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskSerial.requestPort")![0]!;
        var payload = new byte[] { 1, 2, 3 };

        await SerialInterop.Data(id, Convert.ToBase64String(payload)); // bytes ride the boundary base64-encoded

        Assert.Equal(payload, got);
    }

    [Fact]
    public async Task Closing_fires_the_closed_callback_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", true);
        var closed = 0;
        var dataFired = 0;
        await new Serial(js).RequestPortAsync(
            new SerialOptions(),
            _ => { dataFired++; return Task.CompletedTask; },
            () => { closed++; return Task.CompletedTask; });
        var id = (int)js.ArgsFor("__raskSerial.requestPort")![0]!;

        await SerialInterop.Closed(id);

        Assert.Equal(1, closed);

        await SerialInterop.Data(id, Convert.ToBase64String([0])); // routing stopped after close
        Assert.Equal(0, dataFired);
    }

    [Fact]
    public async Task Writing_forwards_the_bytes()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", true);
        var port = await new Serial(js).RequestPortAsync(new SerialOptions(), _ => Task.CompletedTask);
        var id = (int)js.ArgsFor("__raskSerial.requestPort")![0]!;
        var data = new byte[] { 9, 8, 7 };

        await port!.WriteAsync(data);

        var args = js.ArgsFor("__raskSerial.write");
        Assert.Equal(id, args![0]);
        Assert.Equal(Convert.ToBase64String(data), args[1]); // bytes ride the boundary base64-encoded
    }

    [Fact]
    public async Task Disposing_the_port_closes_it_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskSerial.requestPort", true);
        var fired = 0;
        var port = await new Serial(js).RequestPortAsync(new SerialOptions(), _ =>
        {
            fired++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskSerial.requestPort")![0]!;

        await port!.DisposeAsync();

        Assert.Equal([id], js.ArgsFor("__raskSerial.close"));

        await SerialInterop.Data(id, Convert.ToBase64String([0]));
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Requesting_a_port_with_null_arguments_throws()
    {
        var svc = new Serial(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.RequestPortAsync(null!, _ => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await svc.RequestPortAsync(new SerialOptions(), null!));
    }

    [Fact]
    public void A_vendor_only_filter_omits_the_null_product_id()
    {
        // A vendor-only filter must not serialize usbProductId as null — the browser coerces null to 0 and
        // shows an empty chooser. JsonIgnore(WhenWritingNull) drops the absent field. Use Web defaults to
        // mirror the interop's RaskWasmBrowserJsonContext (camelCase) naming.
        var json = JsonSerializer.Serialize(
            new SerialPortFilter(UsbVendorId: 0x2341), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("usbVendorId", json);
        Assert.DoesNotContain("usbProductId", json);
    }
}
