using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
