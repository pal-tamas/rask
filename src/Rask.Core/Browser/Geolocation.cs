using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IGeolocation" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.geolocation.getCurrentPosition</c> is callback-based, so the call goes through the
///     framework's <c>__raskApi.geolocation</c> helper, which wraps it in a Promise.
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
}
