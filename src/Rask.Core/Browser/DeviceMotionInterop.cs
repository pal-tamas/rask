using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IDeviceMotion" /> — routes a pushed reading back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskDeviceMotion</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeviceMotionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<MotionReading, Task>> Handlers = new();

    internal static int Register(Func<MotionReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each motion reading; do not call.</summary>
    [JSInvokable("RaskDeviceMotion")]
    public static Task Reading(int id, MotionReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}
