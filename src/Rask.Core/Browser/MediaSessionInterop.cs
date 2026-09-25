using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IMediaSession" /> — routes a pushed media action back to the right C#
///     callback by handler id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskMediaSession</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediaSessionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<Task>> Handlers = new();

    internal static int Register(Func<Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a registered media action fires; do not call.</summary>
    [JSInvokable("RaskMediaSessionAction")]
    public static Task Invoke(int id) =>
        Handlers.TryGetValue(id, out var handler) ? handler() : Task.CompletedTask;
}
