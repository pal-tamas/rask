using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

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

/// <summary>
///     Infrastructure for <see cref="IBattery" /> — routes a pushed battery change back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskBattery</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BatteryInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<BatteryStatus, Task>> Handlers = new();

    internal static int Register(Func<BatteryStatus, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when the battery status changes; do not call.</summary>
    [JSInvokable("RaskBatteryChanged")]
    public static Task Changed(int id, BatteryStatus status) =>
        Handlers.TryGetValue(id, out var handler) ? handler(status) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IBattery" />, backed by the unified <see cref="IJSRuntime" />. The framework's
///     <c>__raskBattery</c> helper reads <c>navigator.getBattery()</c> and, for a watch, adds the
///     <c>levelchange</c>/<c>chargingchange</c> listeners under the C#-minted id and pushes each update into
///     <see cref="BatteryInterop" />.
/// </summary>
public sealed class Battery : IBattery
{
    private readonly IJSRuntime _js;

    // Root BatteryInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(BatteryInterop))]
    public Battery(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskBattery.isSupported");

    /// <inheritdoc />
    public ValueTask<BatteryStatus?> GetStatusAsync() =>
        _js.InvokeAsync<BatteryStatus?>("__raskBattery.getStatus");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<BatteryStatus, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        // Register before adding the JS listeners so no early change races ahead of the handler.
        var id = BatteryInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskBattery.watch", id);
        }
        catch
        {
            BatteryInterop.Unregister(id);
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
            BatteryInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskBattery.clear", id);
        }
    }
}
