using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
        var hs = await _js.InvokeAsync<UsbDeviceHandshake?>("__raskUsb.requestDevice", (object)(filters ?? [])).ConfigureAwait(false);
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
        var list = await _js.InvokeAsync<UsbDeviceHandshake[]>("__raskUsb.getDevices").ConfigureAwait(false);
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
            var w = await js.InvokeAsync<UsbInTransferWire>("__raskUsb.transferIn", id, endpointNumber, length).ConfigureAwait(false);
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
            var w = await js.InvokeAsync<UsbInTransferWire>("__raskUsb.controlTransferIn", id, setup, length).ConfigureAwait(false);
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
            await js.InvokeVoidAsync("__raskUsb.close", id).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => CloseAsync();

        private void Guard() => ObjectDisposedException.ThrowIf(_closed, typeof(IUsbDevice));
    }
}
