namespace Rask.Core.Browser;

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
///     protected override async Task OnFirstRender()
///     {
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
