namespace Rask.Core.Browser;

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
