using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IBattery" /> — routes a pushed battery change back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskBattery</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BatteryInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<BatteryStatus, Task>> Handlers = new();

    internal static int Register(Func<BatteryStatus, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when the battery status changes; do not call.</summary>
    [JSInvokable("RaskBatteryChanged")]
    public static Task Changed(int id, BatteryStatus status) =>
        Handlers.TryGetValue(id, out var handler) ? handler(status) : Task.CompletedTask;
}
