using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IResizeObserver" /> — routes a pushed size change back to the right
///     C# callback by observation id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskResize</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ResizeInterop
{
    private static readonly JsCallbacks<Func<ResizeEntry, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<ResizeEntry, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's size changes; do not call.</summary>
    [JSInvokable("RaskResizeChanged")]
    public static Task Changed(int id, ResizeEntry entry) =>
        Handlers.TryGet(id, out var handler) ? handler(entry) : Task.CompletedTask;
}
