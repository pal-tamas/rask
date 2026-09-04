using System.Runtime.CompilerServices;
using Rask.Core.Forms;

namespace Rask.Core;

// A third chain SHAPE beside Build<T> and Build<T, TMode>, and it exists for one reason: an indexer
// cannot be constrained, so the only way to offer the submit-state children indexer on a form and
// nowhere else is for the form's chain to be a different type. Putting it on Build<T> would offer
// `Div[submitting => [ … ]]`, which has no submit state to report; putting it on Build<T, TMode> would
// offer it on every form CONTROL, which has no children.
//
// A type argument is the only thing an extension method can discriminate on — the same reasoning that
// makes Bound/Controlled types rather than a bool — so the generator emits the shared surface a third
// time over this shape, and the form's own steps land on it too. Everything else matches Build<T>
// exactly: a readonly struct over the one component reference, the implicit conversion that lets the
// chain read as the component it built, and the children indexers that end it.

/// <summary>
///     The chain of a form: <see cref="Build{T}" /> plus the children indexer that takes the submit
///     state.
/// </summary>
/// <remarks>
///     Reached by <c>Form.Model(model)</c>. Its extra indexer takes a factory rather than a list, so the
///     children can say what a submit in flight looks like —
///     <c>Form.Model(m)[submitting =&gt; [ Button.Disabled(submitting)[ … ] ]]</c> — while the fixed-list
///     indexers it shares with every other chain keep working unchanged.
/// </remarks>
/// <typeparam name="T">The form being built.</typeparam>
public readonly struct FormBuild<T> : IComponentChain
    where T : Component, ISubmitAware
{
    /// <inheritdoc cref="Build{T}(T)" />
    public FormBuild(T component) => Value = component;

    /// <inheritdoc cref="Build{T}.Value" />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public T Value { get; }

    /// <inheritdoc cref="Build{T}.op_Implicit" />
    public static implicit operator T(FormBuild<T> chain) => chain.Value;

    /// <inheritdoc cref="Build{T}.ToHtml" />
    public string ToHtml() => Value.ToHtml();

    /// <inheritdoc cref="Build{T}.this[Component?[]]" />
    [OverloadResolutionPriority(1)]
    public Component this[params Component?[] children] => Value[children];

    /// <inheritdoc cref="Build{T}.this[IEnumerable{Component?}]" />
    [OverloadResolutionPriority(1)]
    public Component this[IEnumerable<Component?> children] => Value[children];

    /// <inheritdoc cref="Component.this[object?[]]" />
    public Component this[params object?[] children] => Value[children];

    /// <summary>
    ///     Gives the form children that depend on whether a submit is in flight, ending the chain.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The factory is stored, not called: it runs on every render, inside the render walk, so a
    ///         component it builds keeps its identity — and its state — across the two renders a submit
    ///         causes. Building the children here instead would freeze them at the state the chain was
    ///         written in, which is always <c>false</c>.
    ///     </para>
    ///     <para>
    ///         It binds without ambiguity beside the three fixed-list indexers because a lambda whose
    ///         parameter is untyped and whose body is a collection expression has NO natural type, so it
    ///         is not a candidate for the <c>params object?[]</c> overload that would otherwise take
    ///         anything.
    ///     </para>
    /// </remarks>
    public Component this[Func<bool, IEnumerable<Component?>> children]
    {
        get
        {
            Value.SetChildrenFactory(children);
            return Value;
        }
    }

    Component IComponentChain.Unwrap() => Value;
}
