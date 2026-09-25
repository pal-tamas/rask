namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the device's physical orientation (the Device Orientation API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Device_orientation_events" />) — the
///     gyroscope/compass angles, e.g. for a tilt-controlled UI, an AR overlay, or a compass. Works on
///     <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The browser <b>pushes</b> each reading to the C# callback (via a static <c>[JSInvokable]</c>, so
///         one wiring serves both transports). Start watching from a lifecycle hook and dispose the returned
///         handle on unmount. A handler that updates state should call <c>StateHasChanged()</c> (it's a
///         subscription, not a render/binding callback, so RASK026 doesn't apply).
///     </para>
///     <para>
///         iOS requires a permission grant from a user gesture: call <see cref="RequestPermissionAsync" />
///         from a click handler before <see cref="WatchAsync" />. On platforms without a prompt it returns
///         <see cref="SensorPermissionState.Granted" />. Needs a secure context (HTTPS or localhost).
///     </para>
/// </remarks>
public interface IDeviceOrientation
{
    /// <summary>Whether the browser exposes device-orientation events (<c>"DeviceOrientationEvent" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Requests sensor access where the platform requires it (iOS' <c>requestPermission()</c>); resolves
    ///     to <see cref="SensorPermissionState.Granted" /> on platforms that don't prompt. Call from a user gesture.
    /// </summary>
    ValueTask<SensorPermissionState> RequestPermissionAsync();

    /// <summary>
    ///     Starts delivering orientation readings to <paramref name="onReading" />. Dispose the returned
    ///     handle to stop.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading);
}
