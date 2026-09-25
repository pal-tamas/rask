using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IResizeObserver" /> — routes a pushed size change back to the right
///     C# callback by observation id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskResize</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ResizeInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<ResizeEntry, Task>> Handlers = new();

    internal static int Register(Func<ResizeEntry, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's size changes; do not call.</summary>
    [JSInvokable("RaskResizeChanged")]
    public static Task Changed(int id, ResizeEntry entry) =>
        Handlers.TryGetValue(id, out var handler) ? handler(entry) : Task.CompletedTask;
}
