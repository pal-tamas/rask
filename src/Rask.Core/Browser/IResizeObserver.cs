using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>One size notification for an observed element (its content-box size, in CSS pixels).</summary>
/// <param name="Width">Content-box width.</param>
/// <param name="Height">Content-box height.</param>
public sealed record ResizeEntry(double Width, double Height);

/// <summary>
///     Typed access to the Resize Observer API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Resize_Observer_API" />) — be notified
///     when an element's size changes, for container-responsive layouts, re-laying-out a canvas/chart, or
///     auto-sizing. The sibling of <see cref="IIntersectionObserver" /> (size vs visibility). Works on
///     <b>both transports</b>; inject it through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The browser <b>pushes</b> each size change to the C# callback (via a static <c>[JSInvokable]</c>,
///         so one wiring serves both transports). Observe from a lifecycle hook and dispose the returned
///         handle on unmount. A callback that updates state should call <c>StateHasChanged()</c> — the same
///         pattern as subscribing to a background feed (a subscription, not a render/binding callback, so
///         RASK026 doesn't apply).
///     </para>
///     <code>
///     private readonly ElementRef _box = ElementRef.New();
///     protected override Component? Render() => Div(Ref: _box)[ ... ];
///     protected override async Task OnRenderedAsync(bool first)
///     {
///         if (!first) return;
///         _obs = await observer.ObserveAsync(_box, size => { _w = size.Width; StateHasChanged(); return Task.CompletedTask; });
///     }
///     </code>
/// </remarks>
public interface IResizeObserver
{
    /// <summary>
    ///     Observes <paramref name="element" /> and invokes <paramref name="onChange" /> whenever its size
    ///     changes (and once initially with the current size). Dispose the returned handle to stop.
    /// </summary>
    ValueTask<IAsyncDisposable> ObserveAsync(ElementRef element, Func<ResizeEntry, Task> onChange);
}

/// <summary>
///     Infrastructure for <see cref="IResizeObserver" /> — routes a pushed size change back to the right
///     C# callback by observation id. <b>Not for application use;</b> invoked only by the framework's
///     <c>__raskResize</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ResizeInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<ResizeEntry, Task>> Handlers = new();

    internal static int Register(Func<ResizeEntry, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's size changes; do not call.</summary>
    [JSInvokable("RaskResizeChanged")]
    public static Task Changed(int id, ResizeEntry entry) =>
        Handlers.TryGetValue(id, out var handler) ? handler(entry) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IResizeObserver" />, backed by the unified <see cref="IJSRuntime" />. The element
///     is handed across as an <see cref="ElementRef" /> (resolved by the JSON reviver); the framework's
///     <c>__raskResize</c> helper holds the live <c>ResizeObserver</c> and calls back into
///     <see cref="ResizeInterop.Changed" /> per change.
/// </summary>
public sealed class ResizeObserverService : IResizeObserver
{
    private readonly IJSRuntime _js;

    // Root ResizeInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(ResizeInterop))]
    public ResizeObserverService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> ObserveAsync(ElementRef element, Func<ResizeEntry, Task> onChange)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(onChange);

        var id = ResizeInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskResize.observe", id, element);
        }
        catch
        {
            ResizeInterop.Unregister(id);
            throw;
        }

        return new Observation(_js, id);
    }

    private sealed class Observation(IJSRuntime js, int id) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ResizeInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskResize.unobserve", id);
        }
    }
}
