using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IBroadcastChannel" /> — routes a pushed JS <c>onmessage</c> back to
///     the right C# handler by connection id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskBroadcast</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BroadcastInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<string, Task>> Handlers = new();

    internal static int Register(Func<string, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a broadcast message arrives; do not call.</summary>
    [JSInvokable("RaskBroadcastReceive")]
    public static Task Receive(int id, string message) =>
        Handlers.TryGetValue(id, out var handler) ? handler(message) : Task.CompletedTask;
}
