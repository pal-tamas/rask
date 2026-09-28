namespace Rask.Core.Browser;

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
///     protected override async Task OnFirstRendered()
///     {
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
