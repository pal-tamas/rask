using Microsoft.JSInterop;

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

/// <summary>
///     Typed access to the Performance API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Performance" />) — a high-resolution
///     monotonic clock and page-load timing, e.g. to measure an operation or report real-user metrics
///     (RUM). Works on <b>both transports</b>; inject it through a component constructor and read from an
///     event handler or lifecycle hook.
/// </summary>
public interface IPerformance
{
    /// <summary>
    ///     A high-resolution timestamp in milliseconds (<c>performance.now()</c>), monotonic and
    ///     sub-millisecond. Subtract two readings to time an operation.
    /// </summary>
    ValueTask<double> NowAsync();

    /// <summary>
    ///     The page's <see cref="NavigationTiming" />, or <c>null</c> if no navigation entry is available
    ///     (<c>performance.getEntriesByType("navigation")</c>).
    /// </summary>
    ValueTask<NavigationTiming?> GetNavigationTimingAsync();
}

/// <summary>
///     Default <see cref="IPerformance" />, backed by the unified <see cref="IJSRuntime" />. Both calls go
///     through the framework's <c>__raskPerf</c> helper — <c>now</c> for a stable <c>this</c> binding, and
///     <c>navigation</c> to pluck the navigation entry's milestones into a plain object.
/// </summary>
public sealed class Performance(IJSRuntime js) : IPerformance
{
    /// <inheritdoc />
    public ValueTask<double> NowAsync() => js.InvokeAsync<double>("__raskPerf.now");

    /// <inheritdoc />
    public ValueTask<NavigationTiming?> GetNavigationTimingAsync() =>
        js.InvokeAsync<NavigationTiming?>("__raskPerf.navigation");
}
