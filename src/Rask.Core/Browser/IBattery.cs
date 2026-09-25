namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Battery Status API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Battery_Status_API" />) — read the
///     device's charge level and charging state, e.g. to defer heavy background work while unplugged or show
///     a battery indicator. Works on <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="GetStatusAsync" /> is a one-shot read; <see cref="WatchAsync" /> subscribes to changes,
///         with the browser <b>pushing</b> each update to the callback (via a static <c>[JSInvokable]</c>, so
///         one wiring serves both transports). Start watching from a lifecycle hook and dispose the returned
///         handle on unmount. A handler that updates state should call <c>StateHasChanged()</c> (it's a
///         subscription, not a render/binding callback, so RASK026 doesn't apply).
///     </para>
///     <para>
///         Browser support is partial (Chromium-family; Firefox/Safari have removed or never shipped it), so
///         gate on <see cref="IsSupportedAsync" /> — <see cref="GetStatusAsync" /> returns <c>null</c> where
///         it's unavailable. Charge/discharge time is reported only where the browser exposes it; the
///         fields are <c>null</c> otherwise.
///     </para>
/// </remarks>
public interface IBattery
{
    /// <summary>Whether the platform exposes battery status (<c>"getBattery" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Reads the current battery status once, or <c>null</c> if the platform doesn't expose it.</summary>
    ValueTask<BatteryStatus?> GetStatusAsync();

    /// <summary>
    ///     Starts delivering a <see cref="BatteryStatus" /> to <paramref name="onChange" /> whenever the level
    ///     or charging state changes. Dispose the returned handle to stop.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange);
}
