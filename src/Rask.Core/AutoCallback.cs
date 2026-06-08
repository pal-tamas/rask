namespace Rask.Core;

/// <summary>
///     Framework-internal wrappers that make a parent-supplied plain delegate prop re-render the
///     parent after a child invokes it — even when the child wraps the delegate in its own lambda
///     or fires it off the DOM-event path. Emitted by <c>ComponentFactoryGenerator</c> around every
///     qualifying event-callback prop (a <see cref="System.Action" />/<see cref="System.Func{Task}" />
///     -shaped delegate on a non-<see cref="Element" /> component), so plain delegates "just work"
///     with no ceremony — the implicit replacement for the old <c>Callback</c> struct.
/// </summary>
/// <remarks>
///     <para>
///         Each <c>Wrap</c> returns a delegate of the <em>same type</em> as the input, so it drops
///         straight into the child's prop. The returned delegate runs the original and then calls
///         <see cref="Component.StateHasChanged" /> on the component that <em>owns</em> the original
///         (its <c>Target</c>), after awaiting any returned <see cref="System.Threading.Tasks.Task" />.
///     </para>
///     <para>
///         The receiver is captured once at wrap time from <c>original.Target as Component</c> — the
///         same heuristic the DOM handler-owner resolution uses. When the target is not a
///         <see cref="Component" /> (a static method, or a lambda closing over a <em>local</em> rather
///         than <c>this</c>), <c>Wrap</c> returns the original delegate unchanged: no extra allocation,
///         and no re-render fires (same limitation the old <c>Callback</c> had — write the lambda
///         inside the component so it captures <c>this</c>).
///     </para>
///     <para>
///         HTML element handlers (<c>Button.OnClick</c>, …) are <em>not</em> wrapped: those are
///         forwarded straight to the DOM, where the existing handler-owner resolution already
///         re-renders the parent for free. The generator restricts wrapping to non-<see cref="Element" />
///         components for exactly this reason (and to keep the render hot path allocation-free).
///     </para>
/// </remarks>
public static class AutoCallback
{
    /// <summary>Wrap a no-arg sync callback so it re-renders its owner after running.</summary>
    public static Action? Wrap(Action? d)
    {
        if (d is null)
        {
            return null;
        }

        if (d.Target is not Component r)
        {
            return d;
        }

        return () =>
        {
            d();
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a one-arg sync callback so it re-renders its owner after running.</summary>
    public static Action<T>? Wrap<T>(Action<T>? d)
    {
        if (d is null)
        {
            return null;
        }

        if (d.Target is not Component r)
        {
            return d;
        }

        return arg =>
        {
            d(arg);
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a no-arg async callback so it awaits, then re-renders its owner.</summary>
    public static Func<Task>? Wrap(Func<Task>? d)
    {
        if (d is null)
        {
            return null;
        }

        if (d.Target is not Component r)
        {
            return d;
        }

        return async () =>
        {
            await d().ConfigureAwait(false);
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a one-arg async callback so it awaits, then re-renders its owner.</summary>
    public static Func<T, Task>? Wrap<T>(Func<T, Task>? d)
    {
        if (d is null)
        {
            return null;
        }

        if (d.Target is not Component r)
        {
            return d;
        }

        return async arg =>
        {
            await d(arg).ConfigureAwait(false);
            r.StateHasChanged();
        };
    }
}
