using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>The outcome of requesting access to a motion/orientation sensor.</summary>
public enum SensorPermission
{
    /// <summary>Access granted (or no prompt was required on this platform).</summary>
    Granted,

    /// <summary>Access denied by the user or the platform.</summary>
    Denied
}

/// <summary>One device-orientation reading (a <c>deviceorientation</c> event).</summary>
/// <param name="Alpha">Rotation around the Z axis, 0–360° (compass heading); <c>null</c> if unavailable.</param>
/// <param name="Beta">Front-to-back tilt, -180–180°; <c>null</c> if unavailable.</param>
/// <param name="Gamma">Left-to-right tilt, -90–90°; <c>null</c> if unavailable.</param>
/// <param name="Absolute">Whether the reading is relative to the Earth's frame (true) or arbitrary (false).</param>
public sealed record OrientationReading(double? Alpha, double? Beta, double? Gamma, bool Absolute);

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
///         <see cref="SensorPermission.Granted" />. Needs a secure context (HTTPS or localhost).
///     </para>
/// </remarks>
public interface IDeviceOrientation
{
    /// <summary>Whether the browser exposes device-orientation events (<c>"DeviceOrientationEvent" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Requests sensor access where the platform requires it (iOS' <c>requestPermission()</c>); resolves
    ///     to <see cref="SensorPermission.Granted" /> on platforms that don't prompt. Call from a user gesture.
    /// </summary>
    ValueTask<SensorPermission> RequestPermissionAsync();

    /// <summary>
    ///     Starts delivering orientation readings to <paramref name="onReading" />. Dispose the returned
    ///     handle to stop.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading);
}

/// <summary>
///     Infrastructure for <see cref="IDeviceOrientation" /> — routes a pushed reading back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskDeviceOrientation</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeviceOrientationInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<OrientationReading, Task>> Handlers = new();

    internal static int Register(Func<OrientationReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge for each orientation reading; do not call.</summary>
    [JSInvokable("RaskDeviceOrientation")]
    public static Task Reading(int id, OrientationReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IDeviceOrientation" />, backed by the unified <see cref="IJSRuntime" />. The
///     framework's <c>__raskDeviceOrientation</c> helper adds the <c>deviceorientation</c> listener under the
///     C#-minted id and pushes each reading into <see cref="DeviceOrientationInterop" />.
/// </summary>
public sealed class DeviceOrientation : IDeviceOrientation
{
    private readonly IJSRuntime _js;

    // Root DeviceOrientationInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(DeviceOrientationInterop))]
    public DeviceOrientation(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskDeviceOrientation.isSupported");

    /// <inheritdoc />
    public async ValueTask<SensorPermission> RequestPermissionAsync() =>
        await _js.InvokeAsync<string>("__raskDeviceOrientation.requestPermission") == "granted"
            ? SensorPermission.Granted
            : SensorPermission.Denied;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<OrientationReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = DeviceOrientationInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskDeviceOrientation.watch", id);
        }
        catch
        {
            DeviceOrientationInterop.Unregister(id);
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
            DeviceOrientationInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskDeviceOrientation.clear", id);
        }
    }
}
