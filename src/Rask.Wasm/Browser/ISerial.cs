using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>Narrows the port chooser to a specific USB device by vendor/product id.</summary>
/// <param name="UsbVendorId">USB vendor id (e.g. <c>0x2341</c> for Arduino). Null to not filter on it.</param>
/// <param name="UsbProductId">USB product id. Null to not filter on it.</param>
public sealed record SerialPortFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsbVendorId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UsbProductId = null);

/// <summary>
///     How to open a serial port — passed to <c>SerialPort.open</c>. Defaults match the most common
///     8-N-1 / 9600-baud configuration used by Arduino-style boards.
/// </summary>
/// <param name="BaudRate">Bits per second (e.g. 9600, 115200). Must match the device.</param>
/// <param name="DataBits">Data bits per frame — 7 or 8.</param>
/// <param name="StopBits">Stop bits per frame — 1 or 2.</param>
/// <param name="Parity">Parity checking — <c>"none"</c>, <c>"even"</c>, or <c>"odd"</c>.</param>
/// <param name="BufferSize">Read/write buffer size in bytes.</param>
/// <param name="FlowControl">Flow control — <c>"none"</c> or <c>"hardware"</c>.</param>
/// <param name="Filters">
///     Optional device filters for the port chooser; when set, only matching devices are offered.
/// </param>
public sealed record SerialOptions(
    int BaudRate = 9600,
    int DataBits = 8,
    int StopBits = 1,
    string Parity = "none",
    int BufferSize = 255,
    string FlowControl = "none",
    IReadOnlyList<SerialPortFilter>? Filters = null);

/// <summary>
///     Typed access to the Web Serial API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Serial_API" />) — talk to a serial
///     device (Arduino / microcontroller, GPS, label printer, USB-to-serial adapter) straight from C# in the
///     browser: open a port, write bytes, and receive a stream of inbound bytes. <b>WASM-only:</b>
///     <c>navigator.serial.requestPort</c> needs <em>transient</em> user activation (a live gesture) and the
///     live port stream, which the Server/WebSocket round-trip can't carry, so it's registered only by the
///     WASM host. Chromium-family only at the time of writing, and a secure context (HTTPS / localhost) is
///     required.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="RequestPortAsync" /> from a user-gesture handler: it shows the browser's port
///         chooser, opens the chosen port per <see cref="SerialOptions" />, starts a read loop, and hands back
///         an <see cref="ISerialPort" />. The live <c>SerialPort</c> is opaque to C#, so the framework holds it
///         JS-side under a minted id. <b>Dispose</b> the handle (or call <see cref="ISerialPort.CloseAsync" />)
///         to stop the read loop and close the port — releasing it for other apps. Gate on
///         <see cref="IsSupportedAsync" /> and wrap calls in try/catch; a chooser dismissal is <em>not</em> an
///         error and surfaces as a <c>null</c> port.
///     </para>
///     <para>
///         Inbound bytes are <b>pushed</b> to <c>onData</c> (via a static <c>[JSInvokable]</c>), and the
///         optional <c>onClosed</c> fires if the port closes on its own (e.g. the device is unplugged). Those
///         callbacks may call <c>StateHasChanged()</c> to re-render — they're subscription callbacks, not
///         render/binding callbacks, so RASK026 doesn't apply.
///     </para>
/// </remarks>
public interface ISerial
{
    /// <summary>Whether the browser supports the Web Serial API (<c>"serial" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the port chooser, opens the chosen port per <paramref name="options" />, and starts a read
    ///     loop that invokes <paramref name="onData" /> with each chunk of inbound bytes. Returns the open
    ///     <see cref="ISerialPort" />, or <c>null</c> if the user dismisses the chooser. <paramref name="onClosed" />
    ///     (optional) fires once if the port closes on its own — e.g. the device is unplugged — so the UI can
    ///     reset. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<ISerialPort?> RequestPortAsync(
        SerialOptions options, Func<byte[], Task> onData, Func<Task>? onClosed = null);
}

/// <summary>A handle to one open serial port. Dispose (or <see cref="CloseAsync" />) to stop reading and close it.</summary>
public interface ISerialPort : IAsyncDisposable
{
    /// <summary>Writes <paramref name="data" /> to the port. Concurrent writes are serialized.</summary>
    ValueTask WriteAsync(byte[] data);

    /// <summary>Stops the read loop and closes the port, releasing it for other applications.</summary>
    ValueTask CloseAsync();
}

/// <summary>
///     Infrastructure for <see cref="ISerial" /> — routes a pushed chunk of inbound bytes (and the
///     port-closed signal) back to the right C# callback by port id. <b>Not for application use;</b> invoked
///     only by the framework's <c>__raskSerial</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SerialInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Callbacks> Registry = new();

    // Mint the id and register the callbacks C#-side BEFORE the JS read loop starts, so a device's first
    // bytes (e.g. an Arduino reset banner) can't arrive before there's a handler to route them to.
    internal static int Register(Func<byte[], Task> onData, Func<Task>? onClosed)
    {
        var id = Interlocked.Increment(ref _nextId);
        Registry[id] = new Callbacks(onData, onClosed);
        return id;
    }

    internal static void Unregister(int id) => Registry.TryRemove(id, out _);

    /// <summary>
    ///     Infrastructure. Invoked by the JS bridge when bytes arrive on a port; do not call. Bytes ride the
    ///     boundary base64-encoded (raw <c>byte[]</c> args don't marshal across the JS bridge).
    /// </summary>
    [JSInvokable("RaskSerialData")]
    public static Task Data(int id, string base64) =>
        Registry.TryGetValue(id, out var cb) ? cb.OnData(Convert.FromBase64String(base64)) : Task.CompletedTask;

    /// <summary>Infrastructure. Invoked by the JS bridge when a port closes on its own; do not call.</summary>
    [JSInvokable("RaskSerialClosed")]
    public static Task Closed(int id) =>
        Registry.TryRemove(id, out var cb) && cb.OnClosed is not null ? cb.OnClosed() : Task.CompletedTask;

    private readonly record struct Callbacks(Func<byte[], Task> OnData, Func<Task>? OnClosed);
}

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
            opened = await _js.InvokeAsync<bool>("__raskSerial.requestPort", id, options);
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
            await js.InvokeVoidAsync("__raskSerial.close", id);
        }

        public ValueTask DisposeAsync() => CloseAsync();
    }
}
