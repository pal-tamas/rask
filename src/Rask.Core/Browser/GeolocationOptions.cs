namespace Rask.Core.Browser;

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
