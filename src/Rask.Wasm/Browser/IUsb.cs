using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
