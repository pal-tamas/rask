namespace Rask.Core.Browser;

/// <summary>
///     One battery reading (a <c>BatteryManager</c> snapshot,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/BatteryManager" />).
/// </summary>
/// <param name="Level">Charge level from <c>0.0</c> (empty) to <c>1.0</c> (full).</param>
/// <param name="Charging">Whether the device is currently charging (or on external power).</param>
/// <param name="ChargingTime">
///     Seconds until fully charged, or <c>null</c> when unknown / already full / not charging (the API's
///     <c>Infinity</c>). Not reported by every backend.
/// </param>
/// <param name="DischargingTime">
///     Seconds until empty, or <c>null</c> when unknown (the API's <c>Infinity</c>). Not reported by every backend.
/// </param>
public sealed record BatteryStatus(double Level, bool Charging, double? ChargingTime, double? DischargingTime);
