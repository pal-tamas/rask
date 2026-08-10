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
///         <see cref="Component" /> (a static method, or a lambda closing over <em>only</em> locals rather
///         than <c>this</c>), <c>Wrap</c> returns the original delegate unchanged: no extra allocation,
///         and no re-render fires (only a static lambda or one closing over locals-only stays unwrapped — write the lambda
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

        if (DelegateOwner.Resolve(d) is not { } r)
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

        if (DelegateOwner.Resolve(d) is not { } r)
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

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return async () =>
        {
            r.MarkDirtyForAsyncHandler();
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

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return async arg =>
        {
            r.MarkDirtyForAsyncHandler();
            await d(arg).ConfigureAwait(false);
            r.StateHasChanged();
        };
    }

    // Named-type overloads — the framework's own components declare callbacks as Callback/CallbackAsync
    // (see Callbacks.cs). Same typed wrapper as above (no DynamicInvoke), so the re-render path stays
    // allocation-equivalent. The Action/Func overloads remain for consumer code that uses standard delegates.

    /// <summary>Wrap a no-arg sync <see cref="Callback" /> so it re-renders its owner after running.</summary>
    public static Callback? Wrap(Callback? d)
    {
        if (d is null)
        {
            return null;
        }

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return () =>
        {
            d();
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a one-arg sync <see cref="Callback{T}" /> so it re-renders its owner after running.</summary>
    public static Callback<T>? Wrap<T>(Callback<T>? d)
    {
        if (d is null)
        {
            return null;
        }

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return arg =>
        {
            d(arg);
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a no-arg <see cref="CallbackAsync" /> so it awaits, then re-renders its owner.</summary>
    public static CallbackAsync? Wrap(CallbackAsync? d)
    {
        if (d is null)
        {
            return null;
        }

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return async () =>
        {
            r.MarkDirtyForAsyncHandler();
            await d().ConfigureAwait(false);
            r.StateHasChanged();
        };
    }

    /// <summary>Wrap a one-arg <see cref="CallbackAsync{T}" /> so it awaits, then re-renders its owner.</summary>
    public static CallbackAsync<T>? Wrap<T>(CallbackAsync<T>? d)
    {
        if (d is null)
        {
            return null;
        }

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        return async arg =>
        {
            r.MarkDirtyForAsyncHandler();
            await d(arg).ConfigureAwait(false);
            r.StateHasChanged();
        };
    }

    /// <summary>
    ///     Wrap a one-argument callback whose delegate type is only known at run time, so it re-renders its
    ///     owner after running — awaiting first when it is asynchronous.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For the properties that <em>fold</em> a typed callback into a bare <see cref="Delegate" />.
    ///         <c>Form.OnValidSubmit</c> is the case: its generic factory takes
    ///         <c>Callback&lt;TModel&gt;</c> and <c>CallbackAsync&lt;TModel&gt;</c>, wraps whichever it was
    ///         given, and stores the result untyped — and <c>Form</c> calls it back with
    ///         <c>DynamicInvoke(model)</c>. A builder chain reaches the same property through a setter that
    ///         has only the property's own type to work with, so without this the wrap silently did not
    ///         happen and the component that owns the handler was never repainted.
    ///     </para>
    ///     <para>
    ///         Sync stays sync: an <see cref="Action{T}" /> wrapper for a void-returning delegate, a
    ///         <see cref="Func{T, TResult}" /> returning <see cref="Task" /> for a task-returning one, so a
    ///         synchronous submit does not acquire an asynchronous hop it did not have. Both are shaped to
    ///         take one argument, which is what the folded call site invokes them with.
    ///     </para>
    ///     <para>
    ///         This is the one <c>Wrap</c> that costs a <c>DynamicInvoke</c> per call, because the typed
    ///         overloads above have a delegate type to invoke directly and this one does not. It is
    ///         confined to folded properties, which are submit-shaped — one invocation per user action, not
    ///         a render hot path — and the call it replaces was already a <c>DynamicInvoke</c>.
    ///     </para>
    /// </remarks>
    public static Delegate? Wrap(Delegate? d)
    {
        if (d is null)
        {
            return null;
        }

        if (DelegateOwner.Resolve(d) is not { } r)
        {
            return d;
        }

        if (typeof(Task).IsAssignableFrom(d.Method.ReturnType))
        {
            return new Func<object?, Task>(async arg =>
            {
                r.MarkDirtyForAsyncHandler();
                if (d.DynamicInvoke(arg) is Task t)
                {
                    await t.ConfigureAwait(false);
                }

                r.StateHasChanged();
            });
        }

        return new Action<object?>(arg =>
        {
            d.DynamicInvoke(arg);
            r.StateHasChanged();
        });
    }
}
