namespace Rask.Core;

/// <summary>
///     A component whose children are its COLUMNS, described by a factory rather than handed over as a
///     fixed list.
/// </summary>
/// <remarks>
///     <para>
///         Implementing it is what puts the component's chain on <see cref="GridBuild{T,TKey}" />, whose
///         children indexer takes <c>c =&gt; [ … ]</c> instead of a list. An interface rather than an
///         attribute, so a component cannot claim the shape without supplying what the shape calls —
///         the same rule <c>ISubmitAware</c> follows for the form chain.
///     </para>
///     <para>
///         The reason the factory exists at all is C# type inference. A column names a member of the row
///         type — <c>c.Field(p =&gt; p.Name)</c> — and a method's type arguments are inferred from its
///         own arguments, never from the target type of the indexer the call sits in. Written as a flat
///         child, <c>UiColumn.Field(p =&gt; p.Name)</c> has nothing to infer <c>p</c> from and does not
///         compile (CS0411). Handing the host itself to a lambda fixes the row type before a single
///         column is written.
///     </para>
/// </remarks>
public interface IColumnHost
{
    /// <summary>
    ///     Gives the component its column factory, replacing any it was given before.
    /// </summary>
    /// <param name="factory">
    ///     A <c>Func&lt;THost, IEnumerable&lt;Component?&gt;&gt;</c> over the implementing type. It runs
    ///     inside the render walk on every render, so the columns it builds keep their identity across
    ///     renders rather than being frozen at the state the chain was written in.
    /// </param>
    /// <remarks>
    ///     Typed as <see cref="Delegate" /> because the interface cannot name the implementing type
    ///     without becoming generic in it, and a generic marker would have to travel through
    ///     <see cref="GridBuild{T,TKey}" /> as a third type parameter that nothing else needs. The
    ///     implementation casts once and calls it directly, so nothing is allocated per render — where
    ///     an adapting closure here would allocate on every chain.
    /// </remarks>
    void SetColumnFactory(Delegate factory);
}
