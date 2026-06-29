using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>A snapshot of one connected gamepad (a <c>Gamepad</c> of the Gamepad API).</summary>
/// <param name="Index">The pad's slot in <c>navigator.getGamepads()</c> (stable for the connection).</param>
/// <param name="Id">The device identification string (controller make/model).</param>
/// <param name="Connected">Whether the pad is currently connected (<c>false</c> on the disconnect reading).</param>
/// <param name="Axes">Analog stick/trigger axes, each <c>-1</c>–<c>1</c> (sticks) — order is device-defined.</param>
/// <param name="Buttons">Per-button analog values, each <c>0</c>–<c>1</c> (<c>1</c> = fully pressed).</param>
public sealed record GamepadReading(
    int Index,
    string Id,
    bool Connected,
    IReadOnlyList<double> Axes,
    IReadOnlyList<double> Buttons);

/// <summary>
///     Typed access to the Gamepad API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Gamepad_API" />) — read connected
///     game controllers (sticks, triggers, buttons) for browser games and interactive experiences. Works
///     on <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The browser exposes no event for button/axis movement, so the framework polls
///         <c>navigator.getGamepads()</c> on <c>requestAnimationFrame</c> and <b>pushes</b> a reading to the
///         C# callback only when a pad's state changes (and on connect/disconnect), throttled so it doesn't
///         flood the transport. Each reading arrives via a static <c>[JSInvokable]</c>, so one wiring serves
///         both transports. Watch from a lifecycle hook and dispose the returned handle on unmount; a
///         callback that updates state should call <c>StateHasChanged()</c> (it's a subscription, not a
///         render/binding callback, so RASK026 doesn't apply).
///     </para>
///     <para>
///         For privacy, a pad only appears after the user has interacted with it (pressed a button). Over the
///         Server transport each reading makes a WebSocket round-trip, so input has network latency — for
///         twitch-sensitive gameplay prefer the WASM transport.
///     </para>
/// </remarks>
public interface IGamepad
{
    /// <summary>Whether the browser supports the Gamepad API (<c>"getGamepads" in navigator</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Starts polling and invokes <paramref name="onReading" /> with a <see cref="GamepadReading" />
    ///     whenever a connected pad's state changes (or it connects/disconnects). Dispose the returned handle
    ///     to stop polling.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchAsync(Func<GamepadReading, Task> onReading);
}

/// <summary>
///     Infrastructure for <see cref="IGamepad" /> — routes a pushed gamepad reading back to the right C#
///     callback by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskGamepad</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GamepadInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<GamepadReading, Task>> Handlers = new();

    internal static int Register(Func<GamepadReading, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when a polled gamepad's state changes; do not call.</summary>
    [JSInvokable("RaskGamepadReading")]
    public static Task Reading(int id, GamepadReading reading) =>
        Handlers.TryGetValue(id, out var handler) ? handler(reading) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IGamepad" />, backed by the unified <see cref="IJSRuntime" />. Each watch gets an
///     integer id; the framework's <c>__raskGamepad</c> helper runs the <c>requestAnimationFrame</c> poll and
///     calls back into <see cref="GamepadInterop.Reading" /> (a static <c>[JSInvokable]</c>, so one wiring
///     serves both transports without marshalling a <c>DotNetObjectReference</c>).
/// </summary>
public sealed class Gamepad : IGamepad
{
    private readonly IJSRuntime _js;

    // Root GamepadInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Reading method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GamepadInterop))]
    public Gamepad(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => _js.InvokeAsync<bool>("__raskGamepad.isSupported");

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> WatchAsync(Func<GamepadReading, Task> onReading)
    {
        ArgumentNullException.ThrowIfNull(onReading);

        var id = GamepadInterop.Register(onReading);
        try
        {
            await _js.InvokeVoidAsync("__raskGamepad.watch", id);
        }
        catch
        {
            GamepadInterop.Unregister(id);
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
            GamepadInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskGamepad.unwatch", id);
        }
    }
}
