namespace Rask.Core;

/// <summary>
///     A handler slot: the sync or the async form of one event, held as one property.
/// </summary>
/// <remarks>
///     <para>
///         Two jobs, and the second is what earns the type. First, it collapses a pair — an event used to
///         be exposed as <c>OnClick</c> (<c>Action?</c>) beside <c>OnClickAsync</c> (<c>Func&lt;Task&gt;?</c>)
///         over ONE storage slot, with a "sync wins" tiebreak at runtime and an error diagnostic to stop
///         you setting both. One property makes that unrepresentable rather than diagnosed.
///     </para>
///     <para>
///         Second, it is NOT a delegate type, and that is what lets a chain step keep the property's name.
///         C# member lookup stops at a delegate-typed property when it resolves <c>x.OnClick(fn)</c> and
///         reads the call as a delegate INVOCATION (CS1593), so extension methods are never considered.
///         A non-invocable, non-delegate member falls through to extension lookup instead — which is what
///         lets the chain receive on the COMPONENT rather than on a wrapper over it.
///     </para>
///     <para>
///         <see cref="Invoke" /> returns <see langword="null" /> when there is nothing to await, so a
///         synchronous handler never acquires an asynchronous hop it did not have: no <c>Task</c>, no
///         closure, no state machine. Call it as
///         <c>if (OnClick?.Invoke() is { } t) await t;</c>. That is the whole reason it is not modelled on
///         Blazor's <c>EventCallback</c>, whose <c>InvokeAsync</c> always hands back a <c>Task</c> and puts
///         the caller on the async path regardless.
///     </para>
///     <para>
///         The delegate is stored bare rather than adapted, so the runtime's handler dispatch keeps
///         type-switching on the shape it always did, and a sync handler stays a <c>System.Action</c> all
///         the way down.
///     </para>
/// </remarks>
public readonly struct Callback
{
    private readonly Delegate? _handler;

    /// <summary>Wraps a synchronous handler.</summary>
    public Callback(Action handler) => _handler = handler;

    /// <summary>Wraps an asynchronous handler, awaited by the renderer before it repaints.</summary>
    public Callback(Func<Task> handler) => _handler = handler;

    internal Callback(Delegate? handler) => _handler = handler;

    /// <summary>The delegate this slot holds. Machinery — invoke the callback instead.</summary>
    /// <remarks>
    ///     Public because the generated setters and the element event store are emitted into, or live in,
    ///     other assemblies and have to reach it; hidden from completion because a handler is meant to be
    ///     called through <see cref="Invoke" />, which is what keeps the sync path off the async one.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Delegate? Handler => _handler;

    /// <summary>Whether a handler was actually wired.</summary>
    public bool HasValue => _handler is not null;

    /// <summary>
    ///     Runs the handler, returning the <see cref="Task" /> to await — or <see langword="null" /> when
    ///     there is nothing to wait for, which is the case for a synchronous handler and for an unset one.
    /// </summary>
    public Task? Invoke() => _handler switch
    {
        Action sync => Run(sync),
        Func<Task> async => async(),
        null => null,
        _ => throw Unexpected(_handler),
    };

    private static Task? Run(Action sync)
    {
        sync();
        return null;
    }

    internal static InvalidOperationException Unexpected(Delegate handler) =>
        new($"A callback slot holds an unsupported delegate shape '{handler.GetType()}'.");
}

/// <inheritdoc cref="Callback" />
/// <typeparam name="T">The argument the event carries.</typeparam>
public readonly struct Callback<T>
{
    private readonly Delegate? _handler;

    /// <inheritdoc cref="Callback(Action)" />
    public Callback(Action<T> handler) => _handler = handler;

    /// <inheritdoc cref="Callback(Func{Task})" />
    public Callback(Func<T, Task> handler) => _handler = handler;

    internal Callback(Delegate? handler) => _handler = handler;

    /// <inheritdoc cref="Callback.Handler" />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Delegate? Handler => _handler;

    /// <inheritdoc cref="Callback.HasValue" />
    public bool HasValue => _handler is not null;

    /// <inheritdoc cref="Callback.Invoke" />
    /// <param name="arg">The argument the event carries.</param>
    public Task? Invoke(T arg) => _handler switch
    {
        Action<T> sync => Run(sync, arg),
        Func<T, Task> async => async(arg),
        null => null,
        _ => throw Callback.Unexpected(_handler),
    };

    private static Task? Run(Action<T> sync, T arg)
    {
        sync(arg);
        return null;
    }
}

/// <inheritdoc cref="Callback" />
/// <remarks>
///     No property in the framework needs two arguments today. It exists so that a component AUTHOR's own
///     two-argument handler has somewhere to go: with the chain receiving on the component, a bare
///     <c>Action&lt;A, B&gt;?</c> property would swallow its own setter (CS1593) and its step would be
///     permanently unreachable. Anything beyond two arguments is reported rather than guessed at.
/// </remarks>
/// <typeparam name="T1">The first argument the event carries.</typeparam>
/// <typeparam name="T2">The second argument the event carries.</typeparam>
public readonly struct Callback<T1, T2>
{
    private readonly Delegate? _handler;

    /// <inheritdoc cref="Callback(Action)" />
    public Callback(Action<T1, T2> handler) => _handler = handler;

    /// <inheritdoc cref="Callback(Func{Task})" />
    public Callback(Func<T1, T2, Task> handler) => _handler = handler;

    internal Callback(Delegate? handler) => _handler = handler;

    /// <inheritdoc cref="Callback.Handler" />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public Delegate? Handler => _handler;

    /// <inheritdoc cref="Callback.HasValue" />
    public bool HasValue => _handler is not null;

    /// <inheritdoc cref="Callback.Invoke" />
    /// <param name="arg1">The first argument the event carries.</param>
    /// <param name="arg2">The second argument the event carries.</param>
    public Task? Invoke(T1 arg1, T2 arg2) => _handler switch
    {
        Action<T1, T2> sync => Run(sync, arg1, arg2),
        Func<T1, T2, Task> async => async(arg1, arg2),
        null => null,
        _ => throw Callback.Unexpected(_handler),
    };

    private static Task? Run(Action<T1, T2> sync, T1 arg1, T2 arg2)
    {
        sync(arg1, arg2);
        return null;
    }
}
