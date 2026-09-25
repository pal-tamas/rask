using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IMutationObserver" /> — routes a pushed mutation back to the right
///     C# callback by observation id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskMutation</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MutationInterop
{
    private static readonly JsCallbacks<Func<MutationEntry, Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<MutationEntry, Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element mutates; do not call.</summary>
    [JSInvokable("RaskMutationChanged")]
    public static Task Changed(int id, MutationEntry entry) =>
        Handlers.TryGet(id, out var handler) ? handler(entry) : Task.CompletedTask;
}
