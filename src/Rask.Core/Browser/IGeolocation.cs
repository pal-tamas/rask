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
