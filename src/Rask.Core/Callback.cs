namespace Rask.Core;

/// <summary>
///     A typed parent→child callback that re-renders the component which *owns* it when invoked,
///     and unifies sync (<see cref="Action" />) and async (<see cref="Func{Task}" />) handlers
///     behind one <see cref="InvokeAsync()" />. (Rask's take on Blazor's <c>EventCallback</c>.)
/// </summary>
/// <remarks>
///     <para>
///         The problem it solves: when a child component invokes a delegate its parent supplied,
///         only the child is auto-dirtied by the event dispatch — the parent that closed over the
///         mutated state is not, so its view goes stale. A <see cref="Callback" /> captures the
///         <em>receiver</em> (the <see cref="Component" /> the delegate belongs to) and calls its
///         <see cref="Component.StateHasChanged" /> after running, so the parent re-renders. (When
///         a child simply <em>forwards</em> a parent delegate straight onto a DOM element, Rask's
///         handler-owner resolution already re-renders the parent — <see cref="Callback" /> is for
///         the cases where the child wraps, transforms, or invokes the callback off the DOM path.)
///     </para>
///     <para>
///         Two usage shapes (a lambda cannot implicitly convert to this struct — C# only lifts a
///         delegate <em>variable</em> — so the typed-prop path takes <see cref="Create{T}(System.Action{T})" />
///         at the call site, while the helper path keeps plain lambdas):
///         <list type="bullet">
///             <item>
///                 <description>
///                     First-class: a child declares <c>Callback&lt;T&gt; OnSelect</c> (an optional
///                     factory param; the generator defaults it to <see cref="Callback{T}.Empty" />)
///                     and calls <c>await OnSelect.InvokeAsync(item)</c>. The parent passes
///                     <c>OnSelect: Callback.Create&lt;Item&gt;(i =&gt; …)</c> (or a delegate variable).
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     Ergonomic: a child declares a plain delegate prop (<c>Action&lt;T&gt;?</c> /
///                     <c>Func&lt;T,Task&gt;?</c>) so call sites pass a bare lambda, and invokes it via
///                     <c>await Callback.InvokeAsync(OnSelect, item)</c>, which re-renders the parent.
///                 </description>
///             </item>
///         </list>
///     </para>
/// </remarks>
public readonly struct Callback
{
    /// <summary>An empty callback. <see cref="InvokeAsync()" /> is a no-op.</summary>
    public static readonly Callback Empty = default;

    private readonly Component? _receiver;
    private readonly Delegate? _delegate;

    internal Callback(Component? receiver, Delegate? @delegate)
    {
        _receiver = receiver;
        _delegate = @delegate;
    }

    /// <summary><c>true</c> when a non-null delegate is wired.</summary>
    public bool HasDelegate => _delegate is not null;

    /// <summary>Run the callback (awaiting if async), then re-render the owning component.</summary>
    public ValueTask InvokeAsync() => CallbackInvoker.InvokeAsync(_receiver, _delegate);

    /// <summary>Lift an <see cref="Action" /> variable, capturing its component as the receiver.</summary>
    public static implicit operator Callback(Action handler) => new(handler.Target as Component, handler);

    /// <summary>Lift a <see cref="Func{Task}" /> variable, capturing its component as the receiver.</summary>
    public static implicit operator Callback(Func<Task> handler) => new(handler.Target as Component, handler);

    /// <summary>Wrap a delegate explicitly (e.g. when its <c>Target</c> is the providing component).</summary>
    public static Callback Create(Action handler) => new(handler.Target as Component, handler);

    /// <summary>Wrap an async delegate explicitly.</summary>
    public static Callback Create(Func<Task> handler) => new(handler.Target as Component, handler);

    /// <summary>Wrap a typed delegate explicitly.</summary>
    public static Callback<T> Create<T>(Action<T> handler) => new(handler.Target as Component, handler);

    /// <summary>Wrap a typed async delegate explicitly.</summary>
    public static Callback<T> Create<T>(Func<T, Task> handler) => new(handler.Target as Component, handler);

    // ---- static helpers for the plain-delegate-prop pattern (no struct needed) ----
    // Each captures the delegate's component Target as the receiver and re-renders it after the
    // callback runs. Null-tolerant: a null callback is a no-op, so the `OnX?.Invoke()` boilerplate
    // and the parent-rerender footgun both disappear behind one call.

    /// <summary>Invoke a nullable no-arg sync callback and re-render its owner.</summary>
    public static ValueTask InvokeAsync(Action? callback) =>
        CallbackInvoker.InvokeAsync(callback?.Target as Component, callback);

    /// <summary>Invoke a nullable no-arg async callback and re-render its owner.</summary>
    public static ValueTask InvokeAsync(Func<Task>? callback) =>
        CallbackInvoker.InvokeAsync(callback?.Target as Component, callback);

    /// <summary>Invoke a nullable one-arg sync callback and re-render its owner.</summary>
    public static ValueTask InvokeAsync<T>(Action<T>? callback, T arg) =>
        CallbackInvoker.InvokeAsync(callback?.Target as Component, callback, arg);

    /// <summary>Invoke a nullable one-arg async callback and re-render its owner.</summary>
    public static ValueTask InvokeAsync<T>(Func<T, Task>? callback, T arg) =>
        CallbackInvoker.InvokeAsync(callback?.Target as Component, callback, arg);
}

/// <summary>
///     A typed one-argument <see cref="Callback" />. See that type's remarks for usage.
/// </summary>
/// <typeparam name="T">The argument the child passes when invoking.</typeparam>
public readonly struct Callback<T>
{
    /// <summary>An empty callback. <see cref="InvokeAsync(T)" /> is a no-op.</summary>
    public static readonly Callback<T> Empty = default;

    private readonly Component? _receiver;
    private readonly Delegate? _delegate;

    internal Callback(Component? receiver, Delegate? @delegate)
    {
        _receiver = receiver;
        _delegate = @delegate;
    }

    /// <summary><c>true</c> when a non-null delegate is wired.</summary>
    public bool HasDelegate => _delegate is not null;

    /// <summary>Run the callback with <paramref name="arg" /> (awaiting if async), then re-render the owner.</summary>
    public ValueTask InvokeAsync(T arg) => CallbackInvoker.InvokeAsync(_receiver, _delegate, arg);

    /// <summary>Lift an <see cref="Action{T}" /> variable, capturing its component as the receiver.</summary>
    public static implicit operator Callback<T>(Action<T> handler) => new(handler.Target as Component, handler);

    /// <summary>Lift a <see cref="Func{T, Task}" /> variable, capturing its component as the receiver.</summary>
    public static implicit operator Callback<T>(Func<T, Task> handler) => new(handler.Target as Component, handler);
}
