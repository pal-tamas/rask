using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Infrastructure for <see cref="IIdleDetector" /> — routes a pushed idle-state change back to the right
///     C# callback by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskIdle</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IdleDetectorInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<IdleReading, Task>> Handlers = new();

    internal static int Register(Func<IdleReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when the idle state changes; do not call.</summary>
    [JSInvokable("RaskIdleChanged")]
    public static Task Changed(int id, IdleReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}
