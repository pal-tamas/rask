using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IBluetooth" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
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

        var hs = await _js.InvokeAsync<BluetoothDeviceHandshake?>("__raskBluetooth.requestDevice", options).ConfigureAwait(false);
        return hs is null ? null : Wrap(hs);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IBluetoothDevice>> GetDevicesAsync()
    {
        var list = await _js.InvokeAsync<BluetoothDeviceHandshake[]>("__raskBluetooth.getDevices").ConfigureAwait(false);
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
            await ReleaseCharacteristicsAsync().ConfigureAwait(false);
            await js.InvokeVoidAsync("__raskBluetooth.disconnect", id).ConfigureAwait(false);
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
                "__raskBluetooth.getCharacteristic", id, serviceUuid, characteristicUuid).ConfigureAwait(false);
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
                await js.InvokeVoidAsync("__raskBluetooth.watchDisconnect", id).ConfigureAwait(false);
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

            // Always evict + drop the GATT link, even if releasing characteristics/watches throws — otherwise
            // the device would leak and the radio would stay connected.
            try
            {
                await ReleaseCharacteristicsAsync().ConfigureAwait(false);

                int[] tokens;
                lock (_disconnectTokens)
                {
                    tokens = [.. _disconnectTokens];
                    _disconnectTokens.Clear();
                }

                foreach (var token in tokens)
                {
                    BluetoothInterop.UnregisterDisconnect(token);
                    await js.InvokeVoidAsync("__raskBluetooth.unwatchDisconnect", id).ConfigureAwait(false);
                }
            }
            finally
            {
                owner.RemoveDevice(id);
                await js.InvokeVoidAsync("__raskBluetooth.release", id).ConfigureAwait(false);
            }
        }

        internal void ForgetCharacteristic(int charId) => _chars.TryRemove(charId, out _);

        private async ValueTask ReleaseCharacteristicsAsync()
        {
            foreach (var ch in _chars.Values)
            {
                await ch.DisposeAsync().ConfigureAwait(false);
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
            await js.InvokeVoidAsync("__raskBluetooth.unwatchDisconnect", id).ConfigureAwait(false);
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
                await owner.RemoveDisconnectWatchAsync(token).ConfigureAwait(false);
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
            var base64 = await js.InvokeAsync<string>("__raskBluetooth.readValue", id).ConfigureAwait(false);
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
                await js.InvokeVoidAsync("__raskBluetooth.startNotifications", id).ConfigureAwait(false);
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
                await js.InvokeVoidAsync("__raskBluetooth.stopNotifications", id).ConfigureAwait(false);
            }

            owner.ForgetCharacteristic(id);
            await js.InvokeVoidAsync("__raskBluetooth.releaseCharacteristic", id).ConfigureAwait(false);
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
            await js.InvokeVoidAsync("__raskBluetooth.stopNotifications", id).ConfigureAwait(false);
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
                await owner.RemoveWatchAsync(token).ConfigureAwait(false);
            }
        }
    }
}
