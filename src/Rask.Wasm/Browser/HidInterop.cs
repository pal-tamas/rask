using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Infrastructure for <see cref="IHid" /> — routes a pushed input report (and the device-disconnect
///     signal) back to the right C# callbacks by device id. <b>Not for application use;</b> invoked only by
///     the framework's <c>__raskHid</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HidInterop
{
    private static int _nextToken;

    // Keyed by a per-watch token (not device id) so several watches on the same physical device — which the
    // browser hands back under one shared id — each get their own callbacks; input/disconnect fan out to
    // every watcher of the device id.
    private static readonly ConcurrentDictionary<int, Watcher> Watchers = new();

    internal static int Register(int deviceId, Func<HidInputReport, Task> onReport, Func<Task>? onDisconnect)
    {
        var token = Interlocked.Increment(ref _nextToken);
        Watchers[token] = new Watcher(deviceId, onReport, onDisconnect);
        return token;
    }

    internal static void Unregister(int token) => Watchers.TryRemove(token, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an input report arrives; do not call.</summary>
    [JSInvokable("RaskHidInputReport")]
    public static async Task Input(int deviceId, int reportId, string base64)
    {
        byte[]? decoded = null;
        foreach (var w in Watchers.Values)
        {
            if (w.DeviceId != deviceId)
            {
                continue;
            }

            decoded ??= Convert.FromBase64String(base64);
            // Each subscriber gets its own copy (a mutating callback must not corrupt the others' bytes), and
            // one throwing callback must not starve the rest of the fan-out.
            var report = new HidInputReport(reportId, (byte[])decoded.Clone());
            try { await w.OnReport(report).ConfigureAwait(false); }
            catch { /* a subscriber's own failure is its concern */ }
        }
    }

    /// <summary>Infrastructure. Invoked by the JS bridge when a watched device is unplugged; do not call.</summary>
    [JSInvokable("RaskHidDisconnected")]
    public static async Task Disconnected(int deviceId)
    {
        foreach (var entry in Watchers)
        {
            // Remove on unplug (the device is gone) so the callback — and the component it captures — is released.
            if (entry.Value.DeviceId == deviceId && Watchers.TryRemove(entry.Key, out var w) && w.OnDisconnect is not null)
            {
                try { await w.OnDisconnect().ConfigureAwait(false); }
                catch { /* isolate subscribers */ }
            }
        }
    }

    private readonly record struct Watcher(int DeviceId, Func<HidInputReport, Task> OnReport, Func<Task>? OnDisconnect);
}
