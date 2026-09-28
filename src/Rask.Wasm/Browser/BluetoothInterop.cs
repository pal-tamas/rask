using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
            // Each subscriber gets its own copy (a mutating callback must not corrupt the others' bytes), and
            // one throwing callback must not starve the rest of the fan-out.
            try { await w.OnValue((byte[])data.Clone()).ConfigureAwait(false); }
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

            try { await w.OnDisconnect().ConfigureAwait(false); }
            catch { /* isolate subscribers */ }
        }
    }

    private readonly record struct ValueWatcher(int CharId, Func<byte[], Task> OnValue);

    private readonly record struct DisconnectWatcher(int DeviceId, Func<Task> OnDisconnect);
}
