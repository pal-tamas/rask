using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Narrows the device chooser. Every field is optional; a set field must match. Null fields are omitted
///     from the request (a serialized <c>null</c> id would coerce to 0 and match nothing).
/// </summary>
/// <param name="VendorId">USB vendor id (e.g. <c>0x2341</c> for Arduino).</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ClassCode">USB device/interface class code.</param>
/// <param name="SubclassCode">USB subclass code.</param>
/// <param name="ProtocolCode">USB protocol code.</param>
/// <param name="SerialNumber">Device serial number.</param>
public sealed record UsbDeviceFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? VendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ClassCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SubclassCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ProtocolCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SerialNumber = null);

/// <summary>Descriptor info for a USB device — what the device reports about itself.</summary>
/// <param name="VendorId">USB vendor id.</param>
/// <param name="ProductId">USB product id.</param>
/// <param name="ManufacturerName">Manufacturer string, if the device exposes one.</param>
/// <param name="ProductName">Product string, if the device exposes one.</param>
/// <param name="SerialNumber">Serial number string, if the device exposes one.</param>
public sealed record UsbDeviceInfo(
    int VendorId, int ProductId, string? ManufacturerName, string? ProductName, string? SerialNumber);

/// <summary>
///     The setup packet for a control transfer (<c>USBControlTransferParameters</c>).
/// </summary>
/// <param name="RequestType"><c>"standard"</c>, <c>"class"</c>, or <c>"vendor"</c>.</param>
/// <param name="Recipient"><c>"device"</c>, <c>"interface"</c>, <c>"endpoint"</c>, or <c>"other"</c>.</param>
/// <param name="Request">The <c>bRequest</c> field.</param>
/// <param name="Value">The <c>wValue</c> field.</param>
/// <param name="Index">The <c>wIndex</c> field.</param>
public sealed record UsbControlTransferParams(
    string RequestType, string Recipient, int Request, int Value, int Index);

/// <summary>Result of an inbound transfer.</summary>
/// <param name="Status"><c>"ok"</c>, <c>"stall"</c>, or <c>"babble"</c>.</param>
/// <param name="Data">The bytes received.</param>
public sealed record UsbTransferResult(string Status, byte[] Data);

/// <summary>Result of an outbound transfer.</summary>
/// <param name="Status"><c>"ok"</c> or <c>"stall"</c>.</param>
/// <param name="BytesWritten">How many bytes the device accepted.</param>
public sealed record UsbOutTransferResult(string Status, int BytesWritten);

/// <summary>
///     Typed access to the WebUSB API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/USB" />) — pair with and drive a USB
///     device (custom hardware, dev boards, instruments) straight from C# in the browser: open it, claim an
///     interface, and run bulk/interrupt/control transfers. <b>WASM-only:</b>
///     <c>navigator.usb.requestDevice</c> needs <em>transient</em> user activation (a live gesture) and the
///     live device handle, which the Server/WebSocket round-trip can't carry, so it's registered only by the
///     WASM host. Chromium-family only at the time of writing, and a secure context (HTTPS / localhost) is
///     required.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="RequestDeviceAsync" /> from a user-gesture handler: it shows the browser's device
///         chooser and returns an <see cref="IUsbDevice" /> (or <c>null</c> if the user dismisses it). The
///         live <c>USBDevice</c> is opaque to C#, so the framework holds it JS-side under a minted id.
///         <see cref="GetDevicesAsync" /> returns devices the user already granted, without a prompt.
///         <b>Dispose</b> the handle (or call <see cref="IUsbDevice.CloseAsync" />) to release the device.
///         Gate on <see cref="IsSupportedAsync" /> and wrap calls in try/catch.
///     </para>
///     <para>Transfer payloads ride the boundary base64-encoded (raw <c>byte[]</c> args don't marshal across the JS bridge).</para>
/// </remarks>
public interface IUsb
{
    /// <summary>Whether the browser supports WebUSB (<c>"usb" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the device chooser (optionally narrowed by <paramref name="filters" /> — <c>null</c>/empty
    ///     means all devices) and returns the chosen <see cref="IUsbDevice" />, or <c>null</c> if the user
    ///     dismisses it. <paramref name="onDisconnect" /> (optional) fires once if the device is later
    ///     unplugged, so the UI can reset. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<IUsbDevice?> RequestDeviceAsync(
        UsbDeviceFilter[]? filters = null, Func<Task>? onDisconnect = null);

    /// <summary>Returns the devices the user has already granted access to (no prompt).</summary>
    ValueTask<IReadOnlyList<IUsbDevice>> GetDevicesAsync();
}

/// <summary>A handle to one USB device. Dispose (or <see cref="CloseAsync" />) to release it.</summary>
public interface IUsbDevice : IAsyncDisposable
{
    /// <summary>What the device reports about itself (available without opening).</summary>
    UsbDeviceInfo Info { get; }

    /// <summary>Opens the device for I/O.</summary>
    ValueTask OpenAsync();

    /// <summary>Selects the device configuration by its <c>configurationValue</c>.</summary>
    ValueTask SelectConfigurationAsync(int configurationValue);

    /// <summary>Claims exclusive use of an interface by number — required before transferring on it.</summary>
    ValueTask ClaimInterfaceAsync(int interfaceNumber);

    /// <summary>Releases a previously claimed interface.</summary>
    ValueTask ReleaseInterfaceAsync(int interfaceNumber);

    /// <summary>Reads up to <paramref name="length" /> bytes from a bulk/interrupt IN endpoint.</summary>
    ValueTask<UsbTransferResult> TransferInAsync(int endpointNumber, int length);

    /// <summary>Writes <paramref name="data" /> to a bulk/interrupt OUT endpoint.</summary>
    ValueTask<UsbOutTransferResult> TransferOutAsync(int endpointNumber, byte[] data);

    /// <summary>Runs a control IN transfer, reading up to <paramref name="length" /> bytes.</summary>
    ValueTask<UsbTransferResult> ControlTransferInAsync(UsbControlTransferParams setup, int length);

    /// <summary>Runs a control OUT transfer, writing <paramref name="data" />.</summary>
    ValueTask<UsbOutTransferResult> ControlTransferOutAsync(UsbControlTransferParams setup, byte[] data);

    /// <summary>Closes the device, releasing it for other applications.</summary>
    ValueTask CloseAsync();
}

/// <summary>
///     Infrastructure for <see cref="IUsb" /> — routes a device-disconnect (unplug) signal back to the right
///     C# callback by device id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskUsb</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class UsbInterop
{
    private static readonly ConcurrentDictionary<int, Func<Task>> Handlers = new();

    internal static void Register(int id, Func<Task> onDisconnect) => Handlers[id] = onDisconnect;

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a paired device is unplugged; do not call.</summary>
    [JSInvokable("RaskUsbDisconnected")]
    public static Task Disconnected(int id) =>
        Handlers.TryRemove(id, out var handler) ? handler() : Task.CompletedTask;
}

/// <summary>Wire shape returned by the JS helper: the minted id plus the device descriptor info.</summary>
internal sealed record UsbDeviceHandshake(int Id, UsbDeviceInfo Info);

/// <summary>Wire shape for an inbound transfer — <c>Data</c> is base64 (raw byte[] doesn't marshal).</summary>
internal sealed record UsbInTransferWire(string Status, string Data);

/// <summary>
///     Default <see cref="IUsb" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>USBDevice</c> is opaque to C#, so the framework's <c>__raskUsb</c> helper holds it under a minted id
///     and the handle drives it by id. Transfer payloads cross base64-encoded.
/// </summary>
public sealed class Usb : IUsb
{
    private readonly IJSRuntime _js;

    // Root UsbInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS DotNetDispatcher
    // (reflection), so without this the Disconnected method could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IUsb" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(UsbInterop))]
    public Usb(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskUsb.isSupported");

    /// <inheritdoc />
    public async ValueTask<IUsbDevice?> RequestDeviceAsync(
        UsbDeviceFilter[]? filters = null, Func<Task>? onDisconnect = null)
    {
        // (object) so the filter array crosses as a single argument, not spread by the params-style overload.
        var hs = await _js.InvokeAsync<UsbDeviceHandshake?>("__raskUsb.requestDevice", (object)(filters ?? []));
        if (hs is null)
        {
            return null;
        }

        if (onDisconnect is not null)
        {
            UsbInterop.Register(hs.Id, onDisconnect);
        }

        return new Device(_js, hs.Id, hs.Info);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IUsbDevice>> GetDevicesAsync()
    {
        var list = await _js.InvokeAsync<UsbDeviceHandshake[]>("__raskUsb.getDevices");
        return list is null ? [] : Array.ConvertAll(list, h => (IUsbDevice)new Device(_js, h.Id, h.Info));
    }

    private sealed class Device(IJSRuntime js, int id, UsbDeviceInfo info) : IUsbDevice
    {
        private bool _closed;

        public UsbDeviceInfo Info => info;

        public ValueTask OpenAsync()
        {
            Guard();
            return js.InvokeVoidAsync("__raskUsb.open", id);
        }

        public ValueTask SelectConfigurationAsync(int configurationValue)
        {
            Guard();
            return js.InvokeVoidAsync("__raskUsb.selectConfiguration", id, configurationValue);
        }

        public ValueTask ClaimInterfaceAsync(int interfaceNumber)
        {
            Guard();
            return js.InvokeVoidAsync("__raskUsb.claimInterface", id, interfaceNumber);
        }

        public ValueTask ReleaseInterfaceAsync(int interfaceNumber)
        {
            Guard();
            return js.InvokeVoidAsync("__raskUsb.releaseInterface", id, interfaceNumber);
        }

        public async ValueTask<UsbTransferResult> TransferInAsync(int endpointNumber, int length)
        {
            Guard();
            var w = await js.InvokeAsync<UsbInTransferWire>("__raskUsb.transferIn", id, endpointNumber, length);
            return new UsbTransferResult(w.Status, Convert.FromBase64String(w.Data));
        }

        public ValueTask<UsbOutTransferResult> TransferOutAsync(int endpointNumber, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            Guard();
            return js.InvokeAsync<UsbOutTransferResult>(
                "__raskUsb.transferOut", id, endpointNumber, Convert.ToBase64String(data));
        }

        public async ValueTask<UsbTransferResult> ControlTransferInAsync(UsbControlTransferParams setup, int length)
        {
            ArgumentNullException.ThrowIfNull(setup);
            Guard();
            var w = await js.InvokeAsync<UsbInTransferWire>("__raskUsb.controlTransferIn", id, setup, length);
            return new UsbTransferResult(w.Status, Convert.FromBase64String(w.Data));
        }

        public ValueTask<UsbOutTransferResult> ControlTransferOutAsync(UsbControlTransferParams setup, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(setup);
            ArgumentNullException.ThrowIfNull(data);
            Guard();
            return js.InvokeAsync<UsbOutTransferResult>(
                "__raskUsb.controlTransferOut", id, setup, Convert.ToBase64String(data));
        }

        public async ValueTask CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            UsbInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskUsb.close", id);
        }

        public ValueTask DisposeAsync() => CloseAsync();

        private void Guard() => ObjectDisposedException.ThrowIf(_closed, typeof(IUsbDevice));
    }
}
