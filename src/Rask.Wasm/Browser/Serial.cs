using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Default <see cref="ISerial" />, backed by the unified <see cref="IJSRuntime" />. The live
///     <c>SerialPort</c> is opaque to C#, so the framework's <c>__raskSerial</c> helper holds it under the
///     C#-minted id and pushes each inbound chunk back into <see cref="SerialInterop.Data" /> (a static
///     <c>[JSInvokable]</c> in this assembly, dispatched by the WASM <c>DotNet</c> shim without a
///     <c>DotNetObjectReference</c>).
/// </summary>
public sealed class Serial : ISerial
{
    private readonly IJSRuntime _js;

    // Root SerialInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="ISerial" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(SerialInterop))]
    public Serial(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskSerial.isSupported");

    /// <inheritdoc />
    public async ValueTask<ISerialPort?> RequestPortAsync(
        SerialOptions options, Func<byte[], Task> onData, Func<Task>? onClosed = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onData);

        // Register before asking JS to open + read, so no inbound byte races ahead of the handler.
        var id = SerialInterop.Register(onData, onClosed);
        bool opened;
        try
        {
            opened = await _js.InvokeAsync<bool>("__raskSerial.requestPort", id, options).ConfigureAwait(false);
        }
        catch
        {
            SerialInterop.Unregister(id);
            throw;
        }

        if (!opened)
        {
            SerialInterop.Unregister(id); // user dismissed the chooser
            return null;
        }

        return new Port(_js, id);
    }

    private sealed class Port(IJSRuntime js, int id) : ISerialPort
    {
        private bool _closed;

        public ValueTask WriteAsync(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            // Bytes ride the boundary base64-encoded — raw byte[] args don't marshal across the JS bridge.
            return js.InvokeVoidAsync("__raskSerial.write", id, Convert.ToBase64String(data));
        }

        public async ValueTask CloseAsync()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            SerialInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskSerial.close", id).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => CloseAsync();
    }
}
