using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IDeviceOrientation" /> — routes a pushed reading back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskDeviceOrientation</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeviceOrientationInterop
{
    private static readonly JsCallbacks<Func<OrientationReading, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<OrientationReading, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge for each orientation reading; do not call.</summary>
    [JSInvokable("RaskDeviceOrientation")]
    public static Task Reading(int id, OrientationReading reading) =>
        Handlers.TryGet(id, out var handler) ? handler(reading) : Task.CompletedTask;
}
