using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     One entry in a <see cref="BluetoothRequestOptions" /> filter list — a device must match every set field.
/// </summary>
/// <param name="Services">Required advertised GATT service UUIDs (name, e.g. <c>"battery_service"</c>, or full UUID).</param>
/// <param name="Name">Exact advertised device name.</param>
/// <param name="NamePrefix">Advertised-name prefix.</param>
public sealed record BluetoothFilter(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Services = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NamePrefix = null);

/// <summary>
///     What to offer in the device chooser. Set <see cref="Filters" /> (and list any services you'll later
///     access in <see cref="OptionalServices" />), or set <see cref="AcceptAllDevices" /> to show everything
///     (still only services in <see cref="OptionalServices" /> are reachable).
/// </summary>
/// <param name="Filters">Device filters; at least one filter or <paramref name="AcceptAllDevices" /> is required.</param>
/// <param name="OptionalServices">Service UUIDs you intend to access but don't filter on.</param>
/// <param name="AcceptAllDevices">Show every nearby device instead of filtering.</param>
public sealed record BluetoothRequestOptions(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BluetoothFilter>? Filters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? OptionalServices = null,
    bool AcceptAllDevices = false);

/// <summary>Identity of a Bluetooth device.</summary>
/// <param name="Id">Stable per-origin device id.</param>
/// <param name="Name">Advertised name, if any.</param>
public sealed record BluetoothDeviceInfo(string Id, string? Name);

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

    /// <summary>Returns the devices the user has already granted access to (no prompt).</summary>
    ValueTask<IReadOnlyList<IBluetoothDevice>> GetDevicesAsync();
}

/// <summary>A handle to one Bluetooth device. Dispose (or <see cref="DisconnectAsync" />) to drop the connection.</summary>
public interface IBluetoothDevice : IAsyncDisposable
{
    /// <summary>The device's identity.</summary>
    BluetoothDeviceInfo Info { get; }

    /// <summary>Connects to the device's GATT server (may be called again after <see cref="DisconnectAsync" />).</summary>
    ValueTask ConnectAsync();

    /// <summary>
    ///     Disconnects the GATT server but keeps the handle usable — call <see cref="ConnectAsync" /> to
    ///     reconnect. Dispose the device to release it entirely.
    /// </summary>
    ValueTask DisconnectAsync();

    /// <summary>Whether the GATT server is currently connected.</summary>
    ValueTask<bool> IsConnectedAsync();

    /// <summary>
    ///     Resolves a characteristic by its service and characteristic UUID (name like <c>"battery_service"</c>
    ///     / <c>"battery_level"</c>, or a full UUID). The device must be connected.
    /// </summary>
    ValueTask<IBluetoothCharacteristic> GetCharacteristicAsync(string serviceUuid, string characteristicUuid);

    /// <summary>
    ///     Invokes <paramref name="onDisconnect" /> if the GATT server disconnects (e.g. the device goes out of
    ///     range or is turned off). Dispose the returned handle to stop listening.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchDisconnectAsync(Func<Task> onDisconnect);
}

/// <summary>A handle to one GATT characteristic. Dispose to stop any notifications and release it.</summary>
public interface IBluetoothCharacteristic : IAsyncDisposable
{
    /// <summary>Reads the characteristic's current value.</summary>
    ValueTask<byte[]> ReadAsync();

    /// <summary>Writes <paramref name="data" /> to the characteristic (with or without a response).</summary>
    ValueTask WriteAsync(byte[] data, bool withResponse = true);

    /// <summary>
    ///     Starts notifications and invokes <paramref name="onValue" /> with each new value the device pushes.
    ///     Dispose the returned handle to stop notifications.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<byte[], Task> onValue);
}

/// <summary>
///     Infrastructure for <see cref="IBluetooth" /> — routes a pushed characteristic value (by characteristic
///     id) and a GATT-disconnect signal (by device id) back to the right C# callbacks. <b>Not for application
///     use;</b> invoked only by the framework's <c>__raskBluetooth</c> JS helper via
///     <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BluetoothInterop
{
    private static int _nextToken;
    private static readonly ConcurrentDictionary<int, ValueWatcher> ValueWatchers = new();
    private static readonly ConcurrentDictionary<int, DisconnectWatcher> DisconnectWatchers = new();

    internal static int RegisterValue(int charId, Func<byte[], Task> onValue)
    {
        var token = Interlocked.Increment(ref _nextToken);
        ValueWatchers[token] = new ValueWatcher(charId, onValue);
        return token;
    }

    internal static int RegisterDisconnect(int deviceId, Func<Task> onDisconnect)
    {
        var token = Interlocked.Increment(ref _nextToken);
        DisconnectWatchers[token] = new DisconnectWatcher(deviceId, onDisconnect);
        return token;
    }

    internal static void UnregisterValue(int token) => ValueWatchers.TryRemove(token, out _);

    internal static void UnregisterDisconnect(int token) => DisconnectWatchers.TryRemove(token, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a characteristic value changes; do not call.</summary>
    [JSInvokable("RaskBluetoothValue")]
    public static async Task Value(int charId, string base64)
    {
        byte[]? data = null;
        foreach (var w in ValueWatchers.Values)
        {
            if (w.CharId != charId)
            {
                continue;
            }

            data ??= Convert.FromBase64String(base64);
            // Isolate subscribers — one throwing callback must not starve the others on a shared notification.
            try { await w.OnValue(data); }
            catch { /* a subscriber's own failure is its concern */ }
        }
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when a device's GATT server disconnects; do not call.</summary>
    [JSInvokable("RaskBluetoothDisconnected")]
    public static async Task Disconnected(int deviceId)
    {
        foreach (var w in DisconnectWatchers.Values)
        {
            if (w.DeviceId != deviceId)
            {
                continue;
            }

            try { await w.OnDisconnect(); }
            catch { /* isolate subscribers */ }
        }
    }

    private readonly record struct ValueWatcher(int CharId, Func<byte[], Task> OnValue);

    private readonly record struct DisconnectWatcher(int DeviceId, Func<Task> OnDisconnect);
}

/// <summary>Wire shape returned by the JS helper for a device: the minted id plus its identity.</summary>
internal sealed record BluetoothDeviceHandshake(int Id, BluetoothDeviceInfo Info);

/// <summary>
///     Default <see cref="IBluetooth" />, backed by the unified <see cref="IJSRuntime" />. The live GATT
///     objects are opaque to C#, so the framework's <c>__raskBluetooth</c> helper holds the device and each
///     resolved characteristic under minted ids and pushes notifications / disconnect back into
///     <see cref="BluetoothInterop" />. Values cross base64-encoded.
/// </summary>
public sealed class Bluetooth : IBluetooth
{
    private readonly IJSRuntime _js;

    // One wrapper per physical device (the browser returns the same device from requestDevice/getDevices), so
    // enumerating doesn't mint duplicate handles and disposing one releases the device exactly once.
    private readonly ConcurrentDictionary<int, Device> _devices = new();

    // Root BluetoothInterop's [JSInvokable]s for the WASM trimmer — they're reached only via the JS
    // DotNetDispatcher (reflection), so without this they could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BluetoothInterop))]
    public Bluetooth(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskBluetooth.isSupported");

    /// <inheritdoc />
    public async ValueTask<IBluetoothDevice?> RequestDeviceAsync(BluetoothRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.AcceptAllDevices && (options.Filters is null || options.Filters.Count == 0))
        {
            throw new ArgumentException(
                "Provide at least one filter or set AcceptAllDevices.", nameof(options));
        }

        var hs = await _js.InvokeAsync<BluetoothDeviceHandshake?>("__raskBluetooth.requestDevice", options);
        return hs is null ? null : Wrap(hs);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IBluetoothDevice>> GetDevicesAsync()
    {
        var list = await _js.InvokeAsync<BluetoothDeviceHandshake[]>("__raskBluetooth.getDevices");
        return list is null ? [] : Array.ConvertAll(list, h => (IBluetoothDevice)Wrap(h));
    }

    private Device Wrap(BluetoothDeviceHandshake hs) =>
        _devices.GetOrAdd(hs.Id, _ => new Device(_js, hs.Id, hs.Info, this));

    private void RemoveDevice(int id) => _devices.TryRemove(id, out _);

    private sealed class Device(IJSRuntime js, int id, BluetoothDeviceInfo info, Bluetooth owner) : IBluetoothDevice
    {
        private readonly HashSet<int> _disconnectTokens = [];
        private readonly ConcurrentDictionary<int, Characteristic> _chars = new();
        private bool _disposed;

        public BluetoothDeviceInfo Info => info;

        public ValueTask ConnectAsync()
        {
            Guard();
            return js.InvokeVoidAsync("__raskBluetooth.connect", id);
        }

        public async ValueTask DisconnectAsync()
        {
            Guard();
            // GATT disconnect invalidates the resolved characteristics — release them so a later reconnect
            // re-resolves fresh ones. The handle itself stays usable.
            await ReleaseCharacteristicsAsync();
            await js.InvokeVoidAsync("__raskBluetooth.disconnect", id);
        }

        public ValueTask<bool> IsConnectedAsync()
        {
            Guard();
            return js.InvokeAsync<bool>("__raskBluetooth.isConnected", id);
        }

        public async ValueTask<IBluetoothCharacteristic> GetCharacteristicAsync(
            string serviceUuid, string characteristicUuid)
        {
            ArgumentException.ThrowIfNullOrEmpty(serviceUuid);
            ArgumentException.ThrowIfNullOrEmpty(characteristicUuid);
            Guard();
            // JS dedups the resolved characteristic to a stable id, so one wrapper backs one physical
            // characteristic — disposing it can't silence a sibling handle's notifications.
            var charId = await js.InvokeAsync<int>(
                "__raskBluetooth.getCharacteristic", id, serviceUuid, characteristicUuid);
            return _chars.GetOrAdd(charId, cid => new Characteristic(js, cid, this));
        }

        public async ValueTask<IAsyncDisposable> WatchDisconnectAsync(Func<Task> onDisconnect)
        {
            ArgumentNullException.ThrowIfNull(onDisconnect);
            Guard();

            var token = BluetoothInterop.RegisterDisconnect(id, onDisconnect);
            lock (_disconnectTokens)
            {
                _disconnectTokens.Add(token);
            }

            try
            {
                await js.InvokeVoidAsync("__raskBluetooth.watchDisconnect", id);
            }
            catch
            {
                BluetoothInterop.UnregisterDisconnect(token);
                lock (_disconnectTokens)
                {
                    _disconnectTokens.Remove(token);
                }

                throw;
            }

            return new DisconnectWatch(this, token);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            await ReleaseCharacteristicsAsync();

            int[] tokens;
            lock (_disconnectTokens)
            {
                tokens = [.. _disconnectTokens];
                _disconnectTokens.Clear();
            }

            foreach (var token in tokens)
            {
                BluetoothInterop.UnregisterDisconnect(token);
                await js.InvokeVoidAsync("__raskBluetooth.unwatchDisconnect", id);
            }

            owner.RemoveDevice(id);
            await js.InvokeVoidAsync("__raskBluetooth.release", id);
        }

        internal void ForgetCharacteristic(int charId) => _chars.TryRemove(charId, out _);

        private async ValueTask ReleaseCharacteristicsAsync()
        {
            foreach (var ch in _chars.Values)
            {
                await ch.DisposeAsync();
            }
        }

        private async ValueTask RemoveDisconnectWatchAsync(int token)
        {
            bool removed;
            lock (_disconnectTokens)
            {
                removed = _disconnectTokens.Remove(token);
            }

            if (!removed)
            {
                return;
            }

            BluetoothInterop.UnregisterDisconnect(token);
            await js.InvokeVoidAsync("__raskBluetooth.unwatchDisconnect", id);
        }

        private void Guard() => ObjectDisposedException.ThrowIf(_disposed, typeof(IBluetoothDevice));

        private sealed class DisconnectWatch(Device owner, int token) : IAsyncDisposable
        {
            private bool _disposed;

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                await owner.RemoveDisconnectWatchAsync(token);
            }
        }
    }

    private sealed class Characteristic(IJSRuntime js, int id, Device owner) : IBluetoothCharacteristic
    {
        private readonly HashSet<int> _tokens = [];
        private bool _disposed;

        public async ValueTask<byte[]> ReadAsync()
        {
            Guard();
            var base64 = await js.InvokeAsync<string>("__raskBluetooth.readValue", id);
            return Convert.FromBase64String(base64);
        }

        public ValueTask WriteAsync(byte[] data, bool withResponse = true)
        {
            ArgumentNullException.ThrowIfNull(data);
            Guard();
            return js.InvokeVoidAsync("__raskBluetooth.writeValue", id, Convert.ToBase64String(data), withResponse);
        }

        public async ValueTask<IAsyncDisposable> WatchAsync(Func<byte[], Task> onValue)
        {
            ArgumentNullException.ThrowIfNull(onValue);
            Guard();

            var token = BluetoothInterop.RegisterValue(id, onValue);
            lock (_tokens)
            {
                _tokens.Add(token);
            }

            try
            {
                await js.InvokeVoidAsync("__raskBluetooth.startNotifications", id);
            }
            catch
            {
                BluetoothInterop.UnregisterValue(token);
                lock (_tokens)
                {
                    _tokens.Remove(token);
                }

                throw;
            }

            return new ValueWatch(this, token);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            int[] tokens;
            lock (_tokens)
            {
                tokens = [.. _tokens];
                _tokens.Clear();
            }

            foreach (var token in tokens)
            {
                BluetoothInterop.UnregisterValue(token);
                await js.InvokeVoidAsync("__raskBluetooth.stopNotifications", id);
            }

            owner.ForgetCharacteristic(id);
            await js.InvokeVoidAsync("__raskBluetooth.releaseCharacteristic", id);
        }

        private async ValueTask RemoveWatchAsync(int token)
        {
            bool removed;
            lock (_tokens)
            {
                removed = _tokens.Remove(token);
            }

            if (!removed)
            {
                return;
            }

            BluetoothInterop.UnregisterValue(token);
            await js.InvokeVoidAsync("__raskBluetooth.stopNotifications", id);
        }

        private void Guard() => ObjectDisposedException.ThrowIf(_disposed, typeof(IBluetoothCharacteristic));

        private sealed class ValueWatch(Characteristic owner, int token) : IAsyncDisposable
        {
            private bool _disposed;

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                await owner.RemoveWatchAsync(token);
            }
        }
    }
}
