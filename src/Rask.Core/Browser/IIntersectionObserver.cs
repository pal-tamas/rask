using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>One intersection notification for an observed element.</summary>
/// <param name="IsIntersecting">Whether the element currently intersects the viewport/root.</param>
/// <param name="Ratio">How much of the element is visible, <c>0</c>–<c>1</c> (<c>intersectionRatio</c>).</param>
public sealed record IntersectionEntry(bool IsIntersecting, double Ratio);

/// <summary>Tuning for an observation (the <c>IntersectionObserver</c> options).</summary>
public sealed record IntersectionOptions
{
    /// <summary>
    ///     Visibility ratios at which the callback fires (e.g. <c>[0, 0.5, 1]</c>). Defaults to firing on
    ///     any enter/leave (<c>0</c>).
    /// </summary>
    public double[]? Thresholds { get; init; }

    /// <summary>Margin grown/shrunk around the root before testing intersection (CSS, e.g. <c>"200px"</c>).</summary>
    public string? RootMargin { get; init; }
}

/// <summary>
///     Typed access to the Intersection Observer API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Intersection_Observer_API" />) — be
///     notified when an element enters or leaves the viewport, for lazy-loading, infinite scroll,
///     reveal-on-scroll, or impression tracking. Works on <b>both transports</b>; inject it through a
///     component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The browser <b>pushes</b> each change to the C# callback (via a static <c>[JSInvokable]</c>, so
///         one wiring serves both transports). Observe from a lifecycle hook and dispose the returned
///         handle on unmount. A callback that updates state should call <c>StateHasChanged()</c> — the
///         same pattern as subscribing to a background feed (it's a subscription, not a render/binding
///         callback, so RASK026 doesn't apply).
///     </para>
///     <code>
///     private readonly ElementRef _sentinel = ElementRef.New();
///     protected override Component? Render() => Div(Ref: _sentinel)[ ... ];
///     protected override async Task OnRenderedAsync(bool first)
///     {
///         if (!first) return;
///         _obs = await observer.ObserveAsync(_sentinel, e => { if (e.IsIntersecting) LoadMore(); return Task.CompletedTask; });
///     }
///     </code>
/// </remarks>
public interface IIntersectionObserver
{
    /// <summary>
    ///     Observes <paramref name="element" /> and invokes <paramref name="onChange" /> whenever its
    ///     intersection with the viewport changes. Dispose the returned handle to stop observing.
    /// </summary>
    ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<IntersectionEntry, Task> onChange, IntersectionOptions? options = null);
}

/// <summary>
///     Infrastructure for <see cref="IIntersectionObserver" /> — routes a pushed intersection change back
///     to the right C# callback by observation id. <b>Not for application use;</b> invoked only by the
///     framework's <c>__raskIntersect</c> JS helper via <c>window.DotNet.invokeMethodAsync</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class IntersectionInterop
{
    private static int _nextId;
    private static readonly ConcurrentDictionary<int, Func<IntersectionEntry, Task>> Handlers = new();

    internal static int Register(Func<IntersectionEntry, Task> handler)
    {
        var id = Interlocked.Increment(ref _nextId);
        Handlers[id] = handler;
        return id;
    }

    internal static void Unregister(int id) => Handlers.TryRemove(id, out _);

    /// <summary>Infrastructure. Invoked by the JS bridge when an observed element's intersection changes; do not call.</summary>
    [JSInvokable("RaskIntersectionChanged")]
    public static Task Changed(int id, IntersectionEntry entry) =>
        Handlers.TryGetValue(id, out var handler) ? handler(entry) : Task.CompletedTask;
}

/// <summary>
///     Default <see cref="IIntersectionObserver" />, backed by the unified <see cref="IJSRuntime" />. The
///     element is handed across as an <see cref="ElementRef" /> (resolved to the live node by the JSON
///     reviver); the framework's <c>__raskIntersect</c> helper holds the live <c>IntersectionObserver</c>
///     and calls back into <see cref="IntersectionInterop.Changed" /> per change.
/// </summary>
public sealed class IntersectionObserverService : IIntersectionObserver
{
    private readonly IJSRuntime _js;

    // Root IntersectionInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNetDispatcher (reflection), so without this the Changed method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(IntersectionInterop))]
    public IntersectionObserverService(IJSRuntime js) => _js = js;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<IntersectionEntry, Task> onChange, IntersectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(onChange);

        var id = IntersectionInterop.Register(onChange);
        try
        {
            await _js.InvokeVoidAsync("__raskIntersect.observe", id, element, options?.Thresholds, options?.RootMargin);
        }
        catch
        {
            IntersectionInterop.Unregister(id);
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
            IntersectionInterop.Unregister(id);
            await js.InvokeVoidAsync("__raskIntersect.unobserve", id);
        }
    }
}
