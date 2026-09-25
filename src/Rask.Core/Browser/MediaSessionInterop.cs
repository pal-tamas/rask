using System.ComponentModel;
using Microsoft.JSInterop;
using Rask.Core.Live;

namespace Rask.Core.Browser;

/// <summary>
///     Infrastructure for <see cref="IMediaSession" /> — routes a pushed media action back to the right C#
///     callback by handler id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskMediaSession</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MediaSessionInterop
{
    private static readonly JsCallbacks<Func<Task>> Handlers = new();

    internal static int Register(IJSRuntime owner, Func<Task> handler) => Handlers.Register(owner, handler);

    internal static void Unregister(int id) => Handlers.Unregister(id);

    /// <summary>Infrastructure. Invoked by the JS bridge when a registered media action fires; do not call.</summary>
    [JSInvokable("RaskMediaSessionAction")]
    public static Task Invoke(int id) =>
        Handlers.TryGet(id, out var handler) ? handler() : Task.CompletedTask;
}
