using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Typed access to the Web Bluetooth API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Bluetooth_API" />) — pair with a
///     Bluetooth Low Energy device and talk to its GATT services from C# in the browser: connect, read / write
///     characteristics, and subscribe to notifications (heart-rate monitors, thermometers, fitness sensors,
///     custom hardware). <b>WASM-only:</b> <c>navigator.bluetooth.requestDevice</c> needs <em>transient</em>
///     user activation (a live gesture) and the live device handle, which the Server/WebSocket round-trip
///     can't carry, so it's registered only by the WASM host. Chromium-family only at the time of writing, and
///     a secure context (HTTPS / localhost) is required.
/// </summary>
/// <remarks>
///     <para>
///         Call <see cref="RequestDeviceAsync" /> from a user-gesture handler, then
///         <see cref="IBluetoothDevice.ConnectAsync" /> and
///         <see cref="IBluetoothDevice.GetCharacteristicAsync" />. The live GATT objects are opaque to C#, so
///         the framework holds them JS-side under minted ids. <see cref="IBluetoothDevice.DisconnectAsync" />
///         drops the GATT link but keeps the handle reusable; <b>dispose</b> the device to release it (and its
///         characteristics) entirely. Gate on <see cref="IsSupportedAsync" /> and wrap calls in try/catch.
///     </para>
///     <para>
///         Characteristic notifications and the device-disconnect signal are <b>pushed</b> to your callbacks
///         (via static <c>[JSInvokable]</c>s) — they may call <c>StateHasChanged()</c> (subscription callbacks,
///         so RASK026 doesn't apply). Values ride the boundary base64-encoded (raw <c>byte[]</c> args don't
///         marshal across the JS bridge).
///     </para>
/// </remarks>
public interface IBluetooth
{
    /// <summary>Whether the browser supports Web Bluetooth (<c>navigator.bluetooth</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the device chooser per <paramref name="options" /> and returns the chosen device, or
    ///     <c>null</c> if the user dismisses it. Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<IBluetoothDevice?> RequestDeviceAsync(BluetoothRequestOptions options);

    /// <summary>
    ///     Returns the devices the user has already granted access to (no prompt). A given physical device maps
    ///     to a single shared handle — <see cref="RequestDeviceAsync" /> and this method return the <em>same</em>
    ///     <see cref="IBluetoothDevice" /> instance for it, so dispose it from a single owner (disposing one
    ///     reference releases the device for all).
    /// </summary>
    ValueTask<IReadOnlyList<IBluetoothDevice>> GetDevicesAsync();
}
