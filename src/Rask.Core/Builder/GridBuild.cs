namespace Rask.Core;

/// <summary>
///     The key type of a grid chain that has not been told what identifies a row.
/// </summary>
/// <remarks>
///     A phantom, and an uninhabited one: no instance of it exists or can be made. It is what
///     <see cref="GridBuild{T,TKey}" /> carries until a <c>RowKey</c> step names the real key type, which
///     is what lets selection be offered ONLY once a row can be identified — rather than offered always
///     and silently doing nothing.
/// </remarks>
public sealed class NoKey
{
    private NoKey()
    {
    }
}

/// <summary>
///     A grid under construction, carrying the type that identifies one of its rows.
/// </summary>
/// <remarks>
///     <para>
///         A fourth chain SHAPE beside <see cref="Build{T}" />, <see cref="Build{T,TMode}" /> and
///         <see cref="FormBuild{T}" />, and it exists for the same reason the third one did: an indexer
///         cannot be constrained, so the only way to offer the column-factory children indexer on a grid
///         and nowhere else is for the grid's chain to be a different type. Putting it on
///         <see cref="Build{T}" /> would offer <c>Div[c =&gt; [ … ]]</c>, which has no columns to
///         describe.
///     </para>
///     <para>
///         <typeparamref name="TKey" /> is a phantom in the same sense <c>TMode</c> is: nothing is stored
///         for it. The chain opens carrying <see cref="NoKey" />, a <c>RowKey</c> step pins it to what the
///         selector returns, and the selection steps are declared only over a pinned one. So a grid that
///         has not said what identifies a row is not a grid whose selection is rejected: it is one where
///         selection is not offered, in completion or at compile time.
///     </para>
///     <para>
///         Everything else matches <see cref="Build{T}" /> exactly — a <c>readonly struct</c> over the one
///         component reference, the implicit conversion that lets the chain read as the component it
///         built, and an indexer that ends it.
///     </para>
/// </remarks>
/// <typeparam name="T">The grid being built.</typeparam>
/// <typeparam name="TKey">What identifies one of its rows, or <see cref="NoKey" /> until one is named.</typeparam>
public readonly struct GridBuild<T, TKey> : IComponentChain
    where T : Component, IColumnHost
{
    /// <inheritdoc cref="Build{T}(T)" />
    public GridBuild(T component) => Value = component;

    /// <inheritdoc cref="Build{T}.Value" />
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public T Value { get; }

    /// <inheritdoc cref="Build{T}.op_Implicit" />
    public static implicit operator T(GridBuild<T, TKey> chain) => chain.Value;

    /// <inheritdoc cref="Build{T}.ToHtml" />
    public string ToHtml() => Value.ToHtml();

    /// <summary>
    ///     Gives the grid its columns, ending the chain.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The lambda's parameter is the grid itself, which is what fixes the row type: a column step
    ///         written on it — <c>c.Field(p =&gt; p.Name)</c> — infers <c>p</c> from the grid's own type
    ///         argument, where the same call written as a flat child has nothing to infer from at all.
    ///         See <see cref="IColumnHost" />.
    ///     </para>
    ///     <para>
    ///         The factory is stored, not called: it runs on every render, inside the render walk, so a
    ///         column it builds keeps its identity — and any state it holds — across renders. Building
    ///         the columns here instead would freeze them at the state the chain was written in.
    ///     </para>
    ///     <para>
    ///         It is the ONLY children indexer on this shape. The fixed-list ones every other chain
    ///         carries are deliberately absent: a grid renders what its columns say and nothing else, so
    ///         a list of children handed to it would have to be silently dropped. Leaving the overload
    ///         out turns that into a compile error instead.
    ///     </para>
    /// </remarks>
    /// <param name="columns">Called with the grid, and returns its columns.</param>
    public Component this[Func<T, IEnumerable<Component?>> columns]
    {
        get
        {
            Value.SetColumnFactory(columns);
            return Value;
        }
    }

    Component IComponentChain.Unwrap() => Value;
}
