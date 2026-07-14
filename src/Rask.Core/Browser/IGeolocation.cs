using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the device's current position (the Geolocation API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Geolocation" />). Inject it through a
///     component constructor and call from an event handler:
///     <code>
///     var pos = await geolocation.GetCurrentPositionAsync();
///     // pos.Latitude, pos.Longitude, pos.Accuracy ...
///     </code>
/// </summary>
/// <remarks>
///     Requires a secure context (HTTPS or localhost) and the user's permission. A denial, timeout, or
///     unavailable sensor surfaces as a <see cref="JSException" /> from the awaited task — catch it.
///     For continuous tracking use <see cref="WatchAsync" /> (<c>watchPosition</c>).
/// </remarks>
public interface IGeolocation
{
    /// <summary>
    ///     Resolves the device's current position once (<c>navigator.geolocation.getCurrentPosition</c>),
    ///     optionally tuned by <paramref name="options" />.
    /// </summary>
    /// <param name="options">Accuracy, timeout, and cache-age preferences; <c>null</c> uses defaults.</param>
    ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null);

    /// <summary>
    ///     Starts tracking the device's position (<c>navigator.geolocation.watchPosition</c>), invoking
    ///     <paramref name="onPosition" /> for the initial fix and every subsequent update. The browser
    ///     <b>pushes</b> each fix to the handler, so a handler that updates state should call
    ///     <c>StateHasChanged()</c> (a subscription, not a render/binding callback). Dispose the returned
    ///     handle to stop watching (<c>clearWatch</c>).
    /// </summary>
    /// <param name="onPosition">Invoked for each position fix.</param>
    /// <param name="options">Accuracy, timeout, and cache-age preferences; <c>null</c> uses defaults.</param>
    ValueTask<IAsyncDisposable> WatchAsync(Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null);
}

/// <summary>
///     Options for a Geolocation request
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/PositionOptions" />).
/// </summary>
public sealed record GeolocationOptions
{
    /// <summary>
    ///     Request the most accurate fix the device can provide (e.g. GPS). More accurate, but slower
    ///     and more power-hungry. Defaults to <c>false</c>.
    /// </summary>
    public bool EnableHighAccuracy { get; init; }

    /// <summary>
    ///     Maximum time to wait for a fix, in milliseconds. <c>null</c> (the default) waits
    ///     indefinitely; the awaited call faults with a timeout error once it elapses.
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    ///     Maximum age, in milliseconds, of a cached fix the browser may return instead of fetching a
    ///     fresh one. <c>0</c> (the default) always fetches a fresh position.
    /// </summary>
    public int MaximumAgeMs { get; init; }
}

/// <summary>
///     A geographic position fix from the Geolocation API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/GeolocationPosition" />). Distances
///     are metres; angles are degrees. Fields the device cannot supply are <c>null</c>.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
/// <param name="Accuracy">Accuracy of the lat/long pair, in metres (always present).</param>
/// <param name="Altitude">Altitude above the WGS84 ellipsoid in metres, or <c>null</c> if unavailable.</param>
/// <param name="AltitudeAccuracy">Accuracy of <paramref name="Altitude" /> in metres, or <c>null</c>.</param>
/// <param name="Heading">Direction of travel in degrees clockwise from true north, or <c>null</c>.</param>
/// <param name="Speed">Ground speed in metres per second, or <c>null</c>.</param>
/// <param name="TimestampMs">Fix time as Unix epoch milliseconds (<c>GeolocationPosition.timestamp</c>).</param>
public sealed record GeolocationPosition(
    double Latitude,
    double Longitude,
    double Accuracy,
    double? Altitude,
    double? AltitudeAccuracy,
    double? Heading,
    double? Speed,
    double TimestampMs);

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
