using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the WebHID API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/WebHID_API" />) — talk to a
///     human-interface device that isn't covered by a higher-level API: gamepads with custom reports,
///     keyboards with extra keys, simulation controls, point-of-sale hardware. Open a device, send output /
///     feature reports, and subscribe to its input-report stream. <b>WASM-only:</b>
///     <c>navigator.hid.requestDevice</c> needs <em>transient</em> user activation (a live gesture) and the
///     live device handle, which the Server/WebSocket round-trip can't carry, so it's registered only by the
///     WASM host. Chromium-family only at the time of writing, and a secure context (HTTPS / localhost) is
///     required.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="RequestDevicesAsync" /> from a user-gesture handler: it shows the browser's chooser
///         and returns the granted devices (possibly several). The live <c>HIDDevice</c> is opaque to C#, so
///         the framework holds it JS-side under a minted id. <see cref="GetDevicesAsync" /> returns devices the
///         user already granted, without a prompt. <b>Dispose</b> a handle (or call
///         <see cref="IHidDevice.CloseAsync" />) to release it. Gate on <see cref="IsSupportedAsync" /> and
///         wrap calls in try/catch.
///     </para>
///     <para>
///         Input reports are <b>pushed</b> to the callback you pass <see cref="IHidDevice.WatchInputReportsAsync" />
///         (via a static <c>[JSInvokable]</c>), along with an optional disconnect signal — those callbacks may
///         call <c>StateHasChanged()</c> to re-render (subscription callbacks, so RASK026 doesn't apply).
///         Report payloads ride the boundary base64-encoded (raw <c>byte[]</c> args don't marshal across the
///         JS bridge).
///     </para>
/// </remarks>
public interface IHid
{
    /// <summary>Whether the browser supports WebHID (<c>"hid" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the device chooser (optionally narrowed by <paramref name="filters" /> — <c>null</c>/empty
    ///     means all devices) and returns the granted devices (zero if the user dismisses it). Must be called
    ///     from a user-gesture handler.
    /// </summary>
    ValueTask<IReadOnlyList<IHidDevice>> RequestDevicesAsync(HidDeviceFilter[]? filters = null);

    /// <summary>Returns the devices the user has already granted access to (no prompt).</summary>
    ValueTask<IReadOnlyList<IHidDevice>> GetDevicesAsync();
}
