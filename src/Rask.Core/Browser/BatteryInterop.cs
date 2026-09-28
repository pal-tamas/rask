using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IBattery" /> — routes a pushed battery change back to the right C#
///     handler by watch id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskBattery</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BatteryInterop
{
    private static readonly JsCallbacks<Func<BatteryStatus, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<BatteryStatus, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when the battery status changes; do not call.</summary>
    [JSInvokable("RaskBatteryChanged")]
    public static Task Changed(int id, BatteryStatus status) =>
        Handlers.TryGet(id, out var handler) ? handler(status) : Task.CompletedTask;
}
