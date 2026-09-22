using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class UsbTests
{
    private static UsbDeviceInfo SampleInfo => new(0x2341, 0x0043, "Acme", "Widget", "SN-1");

    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.isSupported", true);

        Assert.True(await new Usb(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Requesting_a_device_passes_the_filters_as_one_argument_and_returns_the_device_info()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        var filters = new[] { new UsbDeviceFilter(VendorId: 0x2341) };

        var device = await new Usb(js).RequestDeviceAsync(filters);

        Assert.NotNull(device);
        Assert.Equal(SampleInfo, device!.Info);
        var args = js.ArgsFor("__raskUsb.requestDevice");
        Assert.Single(args!);                       // the filter array crosses as a single argument
        Assert.Same(filters, args![0]);
    }

    [Fact]
    public async Task A_cancelled_device_request_returns_null()
    {
        var js = new FakeJsRuntime(); // no canned response → JS returned null (chooser dismissed)

        var device = await new Usb(js).RequestDeviceAsync();

        Assert.Null(device);
    }

    [Fact]
    public async Task Getting_the_devices_returns_a_handle_for_each()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.getDevices", new[]
        {
            new UsbDeviceHandshake(1, SampleInfo),
            new UsbDeviceHandshake(2, SampleInfo with { ProductName = "Other" }),
        });

        var devices = await new Usb(js).GetDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal("Widget", devices[0].Info.ProductName);
        Assert.Equal("Other", devices[1].Info.ProductName);
    }

    [Fact]
    public async Task The_lifecycle_calls_forward_the_id_and_arguments()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(7, SampleInfo));
        var device = await new Usb(js).RequestDeviceAsync();

        await device!.OpenAsync();
        await device.SelectConfigurationAsync(1);
        await device.ClaimInterfaceAsync(2);
        await device.ReleaseInterfaceAsync(2);

        Assert.Equal([7], js.ArgsFor("__raskUsb.open"));
        Assert.Equal([7, 1], js.ArgsFor("__raskUsb.selectConfiguration"));
        Assert.Equal([7, 2], js.ArgsFor("__raskUsb.claimInterface"));
        Assert.Equal([7, 2], js.ArgsFor("__raskUsb.releaseInterface"));
    }

    [Fact]
    public async Task A_transfer_in_decodes_the_base64_payload()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        var bytes = new byte[] { 4, 5, 6 };
        js.SetResponse("__raskUsb.transferIn", new UsbInTransferWire("ok", Convert.ToBase64String(bytes)));
        var device = await new Usb(js).RequestDeviceAsync();

        var result = await device!.TransferInAsync(endpointNumber: 1, length: 64);

        Assert.Equal("ok", result.Status);
        Assert.Equal(bytes, result.Data);
        Assert.Equal([1, 1, 64], js.ArgsFor("__raskUsb.transferIn"));
    }

    [Fact]
    public async Task A_transfer_out_encodes_the_base64_payload()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        js.SetResponse("__raskUsb.transferOut", new UsbOutTransferResult("ok", 3));
        var device = await new Usb(js).RequestDeviceAsync();
        var data = new byte[] { 9, 8, 7 };

        var result = await device!.TransferOutAsync(endpointNumber: 2, data);

        Assert.Equal(3, result.BytesWritten);
        var args = js.ArgsFor("__raskUsb.transferOut");
        Assert.Equal(1, args![0]);
        Assert.Equal(2, args[1]);
        Assert.Equal(Convert.ToBase64String(data), args[2]); // bytes ride the boundary base64-encoded
    }

    [Fact]
    public async Task A_control_transfer_in_passes_the_setup_and_decodes_the_payload()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        var bytes = new byte[] { 1, 2 };
        js.SetResponse("__raskUsb.controlTransferIn", new UsbInTransferWire("ok", Convert.ToBase64String(bytes)));
        var device = await new Usb(js).RequestDeviceAsync();
        var setup = new UsbControlTransferParams("vendor", "device", Request: 1, Value: 2, Index: 0);

        var result = await device!.ControlTransferInAsync(setup, length: 8);

        Assert.Equal(bytes, result.Data);
        var args = js.ArgsFor("__raskUsb.controlTransferIn");
        Assert.Same(setup, args![1]);
        Assert.Equal(8, args[2]);
    }

    [Fact]
    public async Task Disposing_closes_the_device()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(5, SampleInfo));
        var device = await new Usb(js).RequestDeviceAsync();

        await device!.DisposeAsync();

        Assert.Equal([5], js.ArgsFor("__raskUsb.close"));

        await device.DisposeAsync(); // idempotent — no second close call

        Assert.Equal(1, js.CallCount("__raskUsb.close"));
    }

    [Fact]
    public async Task A_disconnect_fires_the_callback_once()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(9, SampleInfo));
        var fired = 0;
        await new Usb(js).RequestDeviceAsync(onDisconnect: () =>
        {
            fired++;
            return Task.CompletedTask;
        });

        await UsbInterop.Disconnected(9);
        await UsbInterop.Disconnected(9); // unregistered after first — no double fire

        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Closing_unregisters_the_disconnect_callback()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(3, SampleInfo));
        var fired = 0;
        var device = await new Usb(js).RequestDeviceAsync(onDisconnect: () =>
        {
            fired++;
            return Task.CompletedTask;
        });

        await device!.DisposeAsync();
        await UsbInterop.Disconnected(3);

        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task Operations_after_dispose_throw_ObjectDisposedException()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        var device = await new Usb(js).RequestDeviceAsync();
        await device!.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await device.OpenAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await device.TransferInAsync(1, 8));
    }

    [Fact]
    public async Task Null_arguments_throw()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskUsb.requestDevice", new UsbDeviceHandshake(1, SampleInfo));
        var device = await new Usb(js).RequestDeviceAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await device!.TransferOutAsync(1, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await device!.ControlTransferOutAsync(null!, []));
    }

    [Fact]
    public void A_vendor_only_filter_omits_null_fields()
    {
        // A null id would serialize as JSON null and the chooser would match nothing. JsonIgnore(WhenWritingNull)
        // drops absent fields. Web defaults mirror the interop's RaskWasmBrowserJsonContext (camelCase) naming.
        var json = JsonSerializer.Serialize(
            new UsbDeviceFilter(VendorId: 0x2341), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("vendorId", json);
        Assert.DoesNotContain("productId", json);
        Assert.DoesNotContain("serialNumber", json);
    }
}
