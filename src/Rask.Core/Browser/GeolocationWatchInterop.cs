using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IGeolocation.WatchAsync" /> — routes a pushed <c>watchPosition</c> fix
///     back to the right C# handler by watch id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskGeoWatch</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeolocationWatchInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<GeolocationPosition, Task>> Handlers = new();

    internal static int Register(Func<GeolocationPosition, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each position fix; do not call.</summary>
    [JSInvokable("RaskGeolocationFix")]
    public static Task Fix(int id, GeolocationPosition position) =>
        Handlers.TryGetValue(id, out var handler) ? handler(position) : Task.CompletedTask;
}
