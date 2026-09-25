namespace Rask.Core.Browser;

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
