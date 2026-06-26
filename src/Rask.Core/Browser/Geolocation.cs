using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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

/// <summary>
///     Default <see cref="IGeolocation" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.geolocation.getCurrentPosition</c> is callback-based, so the call goes through the
///     framework's <c>__raskApi.geolocation</c> helper, which wraps it in a Promise; <c>watchPosition</c>
///     pushes each fix through the <c>__raskGeoWatch</c> helper into <see cref="GeolocationWatchInterop" />.
/// </summary>
public sealed class Geolocation(IJSRuntime js) : IGeolocation
{
    /// <inheritdoc />
    public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
    {
        options ??= new GeolocationOptions();
        // Args map to the helper's (enableHighAccuracy, timeoutMs, maximumAgeMs) signature; a null
        // timeout becomes Infinity on the JS side.
        return js.InvokeAsync<GeolocationPosition>(
            "__raskApi.geolocation",
            options.EnableHighAccuracy,
            options.TimeoutMs,
            options.MaximumAgeMs);
    }

    /// <inheritdoc />
    // Root GeolocationWatchInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Fix method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GeolocationWatchInterop))]
    public async ValueTask<IAsyncDisposable> WatchAsync(
        Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(onPosition);
        options ??= new GeolocationOptions();

        var id = GeolocationWatchInterop.Register(onPosition);
        try
        {
            await js.InvokeVoidAsync(
                "__raskGeoWatch.watch",
                id,
                options.EnableHighAccuracy,
                options.TimeoutMs,
                options.MaximumAgeMs);
        }
        catch
        {
            GeolocationWatchInterop.Unregister(id);
            throw;
        }

        return new Watch(js, id);
    }

    private sealed class Watch(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            GeolocationWatchInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskGeoWatch.clear", id);
        }
    }
}
