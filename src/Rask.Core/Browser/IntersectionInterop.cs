using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IIntersectionObserver" /> — routes a pushed intersection change back
///     to the right C# callback by observation id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskIntersect</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IntersectionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<IntersectionEntry, Task>> Handlers = new();

    internal static int Register(Func<IntersectionEntry, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's intersection changes; do not call.</summary>
    [JSInvokable("RaskIntersectionChanged")]
    public static Task Changed(int id, IntersectionEntry entry) =>
        Handlers.TryGetValue(id, out var handler) ? handler(entry) : Task.CompletedTask;
}
