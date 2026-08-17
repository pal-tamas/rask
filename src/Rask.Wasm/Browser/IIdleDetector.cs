using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Wasm.Browser;

/// <summary>One idle-state change reported by the Idle Detection API.</summary>
/// <param name="UserIdle">
///     Whether the user is idle — no input and no screen interaction for the configured threshold
///     (<c>userState === "idle"</c>).
/// </param>
/// <param name="ScreenLocked">Whether the device screen is locked (<c>screenState === "locked"</c>).</param>
public sealed record IdleReading(bool UserIdle, bool ScreenLocked);

/// <summary>
///     Typed access to the Idle Detection API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/IdleDetector" />) — be notified when the
///     user goes idle (no input for a threshold) or the screen locks, e.g. to auto-lock a session, pause a
///     sync, or update presence in a collaborative app. <b>WASM-only:</b> permission must be requested from
///     <em>transient</em> user activation and the detector needs the live document, which the Server/WebSocket
///     round-trip can't carry, so it's registered only by the WASM host.
/// </summary>
/// <remarks>
///     <para>
///         Gated on the <c>idle-detection</c> permission: call <see cref="RequestPermissionAsync" /> from a
///         user-gesture handler first, and only <see cref="WatchAsync" /> when it returns <c>"granted"</c>.
///         The browser <b>pushes</b> each change to the C# callback (via a static <c>[JSInvokable]</c>). Watch
///         from a lifecycle hook and dispose the returned handle on unmount; a callback that updates state
///         should call <c>StateHasChanged()</c> (it's a subscription, not a render/binding callback, so
///         RASK026 doesn't apply).
///     </para>
/// </remarks>
public interface IIdleDetector
{
    /// <summary>Whether the browser supports the Idle Detection API (<c>"IdleDetector" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Requests the <c>idle-detection</c> permission, returning the resulting state
    ///     (<c>"granted"</c> / <c>"denied"</c>). Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<string> RequestPermissionAsync();

    /// <summary>
    ///     Starts detection and invokes <paramref name="onChange" /> with an <see cref="IdleReading" /> on each
    ///     user/screen state change. <paramref name="thresholdSeconds" /> is the idle threshold (the spec
    ///     enforces a 60-second minimum). Dispose the returned handle to stop. Throws if permission was not
    ///     granted.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<IdleReading, Task> onChange, int thresholdSeconds = 60);
}

/// <summary>
///     Infrastructure for <see cref="IIdleDetector" /> — routes a pushed idle-state change back to the right
///     C# callback by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskIdle</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IdleDetectorInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<IdleReading, Task>> Handlers = new();

    internal static int Register(Func<IdleReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when the idle state changes; do not call.</summary>
    [JSInvokable("RaskIdleChanged")]
    public static Task Changed(int id, IdleReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IIdleDetector" />, backed by the unified <see cref="IJSRuntime" />. Each watch gets
///     an integer id; the framework's <c>__raskIdle</c> helper holds the live <c>IdleDetector</c> and calls
///     back into <see cref="IdleDetectorInterop.Changed" /> (a static <c>[JSInvokable]</c> in this assembly,
///     dispatched by the WASM <c>DotNet</c> shim without a <c>DotNetObjectReference</c>).
/// </summary>
public sealed class IdleDetectorService : IIdleDetector
{
    private readonly IJSRuntime _js;

    // Root IdleDetectorInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    /// <summary>
    ///     Creates the service. Registered for you — inject <see cref="IIdleDetector" /> rather than
    ///     constructing this.
    /// </summary>
    /// <param name="js">The JS interop runtime the wrapper calls through.</param>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(IdleDetectorInterop))]
    public IdleDetectorService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskIdle.isSupported");

    /// <inheritdoc />
    public ValueTask<string> RequestPermissionAsync() =>
        _js.InvokeAsync<string>("__raskIdle.requestPermission");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<IdleReading, Task> onChange, int thresholdSeconds = 60)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        var id = IdleDetectorInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskIdle.watch", id, thresholdSeconds);
        }
        catch
        {
            IdleDetectorInterop.Unregister(id);
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
            IdleDetectorInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskIdle.unwatch", id);
        }
    }
}
