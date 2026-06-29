using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>One device-motion reading (a <c>devicemotion</c> event). Values are <c>null</c> when the device
/// can't report them. Acceleration is in m/s² (excluding gravity); rotation rate is in °/s.</summary>
/// <param name="AccelerationX">Acceleration along the X axis (m/s²).</param>
/// <param name="AccelerationY">Acceleration along the Y axis (m/s²).</param>
/// <param name="AccelerationZ">Acceleration along the Z axis (m/s²).</param>
/// <param name="RotationAlpha">Rotation rate around the Z axis (°/s).</param>
/// <param name="RotationBeta">Rotation rate around the X axis (°/s).</param>
/// <param name="RotationGamma">Rotation rate around the Y axis (°/s).</param>
/// <param name="Interval">Interval, in ms, at which data is obtained from the hardware.</param>
public sealed record MotionReading(
    double? AccelerationX,
    double? AccelerationY,
    double? AccelerationZ,
    double? RotationAlpha,
    double? RotationBeta,
    double? RotationGamma,
    double? Interval);

/// <summary>
///     Typed access to the device's motion sensors (the Device Motion API,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/API/Device_orientation_events" />) — the
///     accelerometer and gyroscope, e.g. for shake-to-undo, a step counter, or a motion-driven game. Works
///     on <b>both transports</b>; inject it through a component constructor.
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
///         <see cref="SensorPermission.Granted" />. Needs a secure context (HTTPS or localhost).
///     </para>
/// </remarks>
public interface IDeviceMotion
{
    /// <summary>Whether the browser exposes device-motion events (<c>"DeviceMotionEvent" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Requests sensor access where the platform requires it (iOS' <c>requestPermission()</c>); resolves
    ///     to <see cref="SensorPermission.Granted" /> on platforms that don't prompt. Call from a user gesture.
    /// </summary>
    ValueTask<SensorPermission> RequestPermissionAsync();

    /// <summary>
    ///     Starts delivering motion readings to <paramref name="onReading" />. Dispose the returned handle to
    ///     stop.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<MotionReading, Task> onReading);
}

/// <summary>
///     Infrastructure for <see cref="IDeviceMotion" /> — routes a pushed reading back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskDeviceMotion</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeviceMotionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<MotionReading, Task>> Handlers = new();

    internal static int Register(Func<MotionReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each motion reading; do not call.</summary>
    [JSInvokable("RaskDeviceMotion")]
    public static Task Reading(int id, MotionReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IDeviceMotion" />, backed by the unified <see cref="IJSRuntime" />. The framework's
///     <c>__raskDeviceMotion</c> helper adds the <c>devicemotion</c> listener under the C#-minted id and pushes
///     each reading into <see cref="DeviceMotionInterop" />.
/// </summary>
public sealed class DeviceMotion : IDeviceMotion
{
    private readonly IJSRuntime _js;

    // Root DeviceMotionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(DeviceMotionInterop))]
    public DeviceMotion(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskDeviceMotion.isSupported");

    /// <inheritdoc />
    public async ValueTask<SensorPermission> RequestPermissionAsync() =>
        await _js.InvokeAsync<string>("__raskDeviceMotion.requestPermission") == "granted"
            ? SensorPermission.Granted
            : SensorPermission.Denied;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<MotionReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = DeviceMotionInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskDeviceMotion.watch", id);
        }
        catch
        {
            DeviceMotionInterop.Unregister(id);
            throw;
        }

        return new Watch(_js, id);
    }

    private sealed class Watch(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DeviceMotionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskDeviceMotion.clear", id);
        }
    }
}
