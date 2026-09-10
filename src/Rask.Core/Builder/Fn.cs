namespace Rask.Core;

/// <summary>
///     A value the framework ASKS a component for, rather than a handler the user triggers: a template
///     that renders an item, a selector that picks a value, a predicate that filters one.
/// </summary>
/// <remarks>
///     <para>
///         It exists for the same reason <see cref="Callback" /> does — a delegate-typed property swallows
///         its own chain step (CS1593), and a struct over one is not a delegate, so member lookup falls
///         through to the setter. But it is deliberately a SEPARATE family, because the two are different
///         things and the framework already treats them differently.
///     </para>
///     <para>
///         A <see cref="Callback" /> is fired once per interaction, may be sync or async, and re-renders
///         the parent afterwards. An <c>Fn</c> is called during <c>Render()</c>, once per item, and must
///         NOT re-render — auto-wrapping one to call <c>StateHasChanged</c> would render from inside a
///         render. That is why the auto-callback machinery only ever wraps void- or
///         <see cref="Task" />-returning shapes, and it is the line this type keeps on the right side of.
///     </para>
///     <para>
///         Because the framework only ever CALLS it, and a value cannot be produced asynchronously mid-render,
///         there is no async twin to hide — so unlike <see cref="Callback" /> this holds the delegate at its
///         real type and needs no runtime type switch. That keeps the per-item cost to a null check and a
///         call, which is what <c>?.Invoke()</c> already compiles to.
///     </para>
///     <para>
///         They cannot be one family with <see cref="Callback" />: <c>Callback&lt;int, int&gt;</c> (two
///         arguments, returning nothing) and a one-argument shape returning <c>int</c> would collide on
///         arity, so the return type has to be carried by a distinct name.
///     </para>
/// </remarks>
/// <typeparam name="TOut">What the component is asked for.</typeparam>
public readonly struct Fn<TOut>
{
    private readonly Func<TOut>? _fn;

    /// <summary>Wraps the function the framework will call.</summary>
    public Fn(Func<TOut> fn) => _fn = fn;

    /// <summary>Whether a function was actually supplied.</summary>
    public bool HasValue => _fn is not null;

    /// <summary>Calls it, or hands back the default when nothing was supplied.</summary>
    public TOut? Invoke() => _fn is { } fn ? fn() : default;
}

/// <inheritdoc cref="Fn{TOut}" />
/// <typeparam name="TIn">What the framework hands in — the item being templated, say.</typeparam>
/// <typeparam name="TOut">What the component is asked for.</typeparam>
public readonly struct Fn<TIn, TOut>
{
    private readonly Func<TIn, TOut>? _fn;

    /// <inheritdoc cref="Fn{TOut}(Func{TOut})" />
    public Fn(Func<TIn, TOut> fn) => _fn = fn;

    /// <inheritdoc cref="Fn{TOut}.HasValue" />
    public bool HasValue => _fn is not null;

    /// <inheritdoc cref="Fn{TOut}.Invoke" />
    /// <param name="arg">What the framework hands in.</param>
    public TOut? Invoke(TIn arg) => _fn is { } fn ? fn(arg) : default;
}

/// <inheritdoc cref="Fn{TOut}" />
/// <typeparam name="T1">The first thing the framework hands in.</typeparam>
/// <typeparam name="T2">The second — a retry action, or a dismiss callback.</typeparam>
/// <typeparam name="TOut">What the component is asked for.</typeparam>
public readonly struct Fn<T1, T2, TOut>
{
    private readonly Func<T1, T2, TOut>? _fn;

    /// <inheritdoc cref="Fn{TOut}(Func{TOut})" />
    public Fn(Func<T1, T2, TOut> fn) => _fn = fn;

    /// <inheritdoc cref="Fn{TOut}.HasValue" />
    public bool HasValue => _fn is not null;

    /// <inheritdoc cref="Fn{TOut}.Invoke" />
    /// <param name="arg1">The first thing the framework hands in.</param>
    /// <param name="arg2">The second.</param>
    public TOut? Invoke(T1 arg1, T2 arg2) => _fn is { } fn ? fn(arg1, arg2) : default;
}
