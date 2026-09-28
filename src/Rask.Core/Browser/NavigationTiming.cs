namespace Rask.Core.Browser;

/// <summary>
///     Page-load timing milestones from the Navigation Timing API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/PerformanceNavigationTiming" />). All
///     values are milliseconds since the navigation started; a milestone not yet reached reads <c>0</c>.
/// </summary>
/// <param name="TimeToFirstByteMs">When the first response byte arrived (<c>responseStart</c>) — TTFB.</param>
/// <param name="DomInteractiveMs">When the DOM finished parsing (<c>domInteractive</c>).</param>
/// <param name="DomContentLoadedMs">When <c>DOMContentLoaded</c> finished (<c>domContentLoadedEventEnd</c>).</param>
/// <param name="LoadMs">When the <c>load</c> event finished (<c>loadEventEnd</c>).</param>
/// <param name="DurationMs">Total navigation duration (<c>duration</c>).</param>
public sealed record NavigationTiming(
    double TimeToFirstByteMs,
    double DomInteractiveMs,
    double DomContentLoadedMs,
    double LoadMs,
    double DurationMs);
