using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IDeviceMotion" /> — routes a pushed reading back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskDeviceMotion</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeviceMotionInterop
{
    private static readonly JsCallbacks<Func<MotionReading, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<MotionReading, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge for each motion reading; do not call.</summary>
    [JSInvokable("RaskDeviceMotion")]
    public static Task Reading(int id, MotionReading reading) =>
        Handlers.TryGet(id, out var handler) ? handler(reading) : Task.CompletedTask;
}
