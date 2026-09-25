using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Infrastructure for <see cref="IUsb" /> — routes a device-disconnect (unplug) signal back to the right
///     C# callback by device id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskUsb</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class UsbInterop
{
    private static readonly ConcurrentDictionary<int, Func<Task>> Handlers = new();

    internal static void Register(int id, Func<Task> onDisconnect) => Handlers[id] = onDisconnect;

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a paired device is unplugged; do not call.</summary>
    [JSInvokable("RaskUsbDisconnected")]
    public static Task Disconnected(int id) =>
        Handlers.TryRemove(id, out var handler) ? handler() : Task.CompletedTask;
}
