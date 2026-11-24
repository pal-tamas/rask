namespace Rask.Data;

/// <summary>
///     Of the <typeparamref name="TChild" /> rows the aggregate holds, at most one is in force:
///     <c>Period : Aggregate&lt;PeriodId, OneInForce&lt;PeriodFiling&gt;&gt;</c>, the child saying when one is with
///     <c>b.InForceWhile(f =&gt; f.UnfiledAt == null)</c> in its own <c>Configure</c>.
/// </summary>
/// <remarks>
///     <para>
///         The rows no longer in force are the history, and stay. The rule is a unique index on the child's key to
///         this aggregate over the rows in force only — a filtered index on SQL Server, a partial one on PostgreSQL
///         and SQLite — so of two saves that each put one in force, one commits and the other is refused with a
///         <c>RaskValidationException</c> under the new child's row, carrying the child's message.
///     </para>
///     <para>
///         The aggregate reads the one in force with <c>Filings.InForce()</c>, and replaces it with
///         <c>Filings.Put(next, end: f =&gt; f.Unfile(…))</c>, which ends the one in force before it adds the next.
///     </para>
/// </remarks>
/// <typeparam name="TChild">The child of which one is in force.</typeparam>
#pragma warning disable S2326 // the type argument IS the declaration, read by the generator
public abstract class OneInForce<TChild> : IAggregateTrait
    where TChild : class, IEntity;
#pragma warning restore S2326
