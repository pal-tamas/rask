using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>
///     Infrastructure for <see cref="IBackgroundSync" /> — the entry point the service worker's forwarded
///     sync reaches. <b>Not for application use;</b> invoked only by the framework's <c>__raskSync</c> JS
///     helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BackgroundSyncInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<BackgroundSyncEvent, Task>> Handlers = new();

    internal static int Register(Func<BackgroundSyncEvent, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a sync fires; do not call.</summary>
    /// <param name="periodic">Whether this came from <c>periodicsync</c> rather than <c>sync</c>.</param>
    /// <param name="tag">The registered tag the browser woke the app for.</param>
    [JSInvokable("RaskBackgroundSync")]
    public static Task Fired(bool periodic, string tag)
    {
        // One tag can legitimately interest several components (a draft queue and a badge count, say), so
        // every handler sees it — unlike the id-keyed device wrappers, where an event belongs to one watch.
        var reading = new BackgroundSyncEvent(tag, periodic);
        var handlers = Handlers.Values.ToArray();
        return handlers.Length == 0 ? Task.CompletedTask : Task.WhenAll(handlers.Select(h => h(reading)));
    }
}
