using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Components;

/// <summary>
///     Infrastructure for the gesture bridge — routes a result the client posts after running a gesture
///     capability back to the right C# callback by id. <b>Not for application use;</b> invoked only by the
///     framework client via <c>window.DotNet.invokeMethodAsync("Rask.Core", "RaskGestureResult", …)</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GestureResultInterop
{
    // A gesture result handler is one-shot: it's removed when its result posts back (Result). But a trigger
    // that re-renders — or is never clicked — leaves its superseded rid orphaned, since the DOM only ever
    // carries the latest. Cap the live map by evicting the entry this many registrations back: by then its
    // rid is long gone from every client's DOM, so it can never fire. High enough that a genuinely-pending
    // handler is never evicted before its click, yet it bounds the static map instead of leaking per-render.
    private const int Capacity = 65536;

    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<string?, Task>> Handlers = new();

    internal static int Register(Func<string?, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        Handlers.TryRemove(id - Capacity, out _);
        return id;
    }

    /// <summary>Infrastructure. Invoked by the client with a gesture's result (one-shot); do not call.</summary>
    [JSInvokable("RaskGestureResult")]
    public static Task Result(int id, string? value) =>
        Handlers.TryRemove(id, out var handler) ? handler(value) : Task.CompletedTask;
}
