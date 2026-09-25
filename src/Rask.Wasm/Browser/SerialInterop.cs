using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

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
