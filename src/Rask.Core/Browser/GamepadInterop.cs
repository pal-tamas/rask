using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IGamepad" /> — routes a pushed gamepad reading back to the right C#
///     callback by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskGamepad</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GamepadInterop
{
    private static readonly JsCallbacks<Func<GamepadReading, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<GamepadReading, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when a polled gamepad's state changes; do not call.</summary>
    [JSInvokable("RaskGamepadReading")]
    public static Task Reading(int id, GamepadReading reading) =>
        Handlers.TryGet(id, out var handler) ? handler(reading) : Task.CompletedTask;
}
