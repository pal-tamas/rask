namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Mutation Observer API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/MutationObserver" />) — be notified
///     when an element's children, attributes, or text content change, e.g. to react to DOM written by a
///     third-party script or a portal you don't own. Works on <b>both transports</b>; inject it through a
///     component constructor. Completes the observer family alongside <see cref="IIntersectionObserver" />
///     and <see cref="IResizeObserver" />.
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
///     private readonly ElementRef _target = ElementRef.New();
///     protected override Component? Render() => Div(Ref: _target)[ ... ];
///     protected override async Task OnFirstRendered()
///     {
///         _obs = await observer.ObserveAsync(_target, m => { _count++; StateHasChanged(); return Task.CompletedTask; },
///             new MutationOptions { ChildList = true, Attributes = true, Subtree = true });
///     }
///     </code>
/// </remarks>
public interface IMutationObserver
{
    /// <summary>
    ///     Observes <paramref name="element" /> and invokes <paramref name="onChange" /> whenever its
    ///     DOM (children, attributes, or text — per <paramref name="options" />) changes. Dispose the
    ///     returned handle to stop observing.
    /// </summary>
    ValueTask<IAsyncDisposable> ObserveAsync(
        ElementRef element, Func<MutationEntry, Task> onChange, MutationOptions? options = null);
}
