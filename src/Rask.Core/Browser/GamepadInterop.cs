using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
