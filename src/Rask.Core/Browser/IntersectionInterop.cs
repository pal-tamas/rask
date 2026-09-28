using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IIntersectionObserver" /> — routes a pushed intersection change back
///     to the right C# callback by observation id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskIntersect</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IntersectionInterop
{
    private static readonly JsCallbacks<Func<IntersectionEntry, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<IntersectionEntry, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's intersection changes; do not call.</summary>
    [JSInvokable("RaskIntersectionChanged")]
    public static Task Changed(int id, IntersectionEntry entry) =>
        Handlers.TryGet(id, out var handler) ? handler(entry) : Task.CompletedTask;
}
