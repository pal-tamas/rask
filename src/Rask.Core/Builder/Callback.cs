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
///         <see cref="Invoke" /> always returns a <see cref="Task" />, and for a synchronous or unset handler
///         it is the cached, already-completed <see cref="Task.CompletedTask" /> — no allocation, and awaiting
///         it does not yield, so a synchronous handler never acquires an asynchronous hop it did not have.
///         Fire an event with <c>await OnClick.Invoke();</c> and forward one with
///         <c>.OnClick(() => OnRate.Invoke(i))</c>: <see cref="CallbackExtensions" /> gives the nullable
///         property the same <c>Invoke</c>, so an unset slot needs no <c>?.</c> and no <c>??</c>.
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
    ///     Runs the handler and returns the <see cref="Task" /> to await — the cached
    ///     <see cref="Task.CompletedTask" /> for a synchronous handler and for an unset one.
    /// </summary>
    public Task Invoke() => _handler switch
    {
        Action syncHandler => Run(syncHandler),
        Func<Task> asyncHandler => asyncHandler(),
        null => Task.CompletedTask,
        _ => throw Unexpected(_handler),
    };

    private static Task Run(Action syncHandler)
    {
        syncHandler();
        return Task.CompletedTask;
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
    public Task Invoke(T arg) => _handler switch
    {
        Action<T> syncHandler => Run(syncHandler, arg),
        Func<T, Task> asyncHandler => asyncHandler(arg),
        null => Task.CompletedTask,
        _ => throw Callback.Unexpected(_handler),
    };

    private static Task Run(Action<T> syncHandler, T arg)
    {
        syncHandler(arg);
        return Task.CompletedTask;
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
    public Task Invoke(T1 arg1, T2 arg2) => _handler switch
    {
        Action<T1, T2> syncHandler => Run(syncHandler, arg1, arg2),
        Func<T1, T2, Task> asyncHandler => asyncHandler(arg1, arg2),
        null => Task.CompletedTask,
        _ => throw Callback.Unexpected(_handler),
    };

    private static Task Run(Action<T1, T2> syncHandler, T1 arg1, T2 arg2)
    {
        syncHandler(arg1, arg2);
        return Task.CompletedTask;
    }
}
