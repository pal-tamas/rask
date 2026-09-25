namespace Rask.Core.Browser;

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
