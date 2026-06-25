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
///     Continuous tracking (<c>watchPosition</c>) is not yet wrapped.
/// </remarks>
public interface IGeolocation
{
    /// <summary>
    ///     Resolves the device's current position once (<c>navigator.geolocation.getCurrentPosition</c>),
    ///     optionally tuned by <paramref name="options" />.
    /// </summary>
    /// <param name="options">Accuracy, timeout, and cache-age preferences; <c>null</c> uses defaults.</param>
    ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null);
}
