using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     A running number Rask keeps for every row, counted within a group in a declared order, with no SQL in the app:
///     <c>b.Numbered(f =&gt; f.Sequence, within: f =&gt; f.CountedAt, by: f =&gt; new { f.Moment, f.Number, f.Id })</c>.
/// </summary>
/// <remarks>
///     <para>
///         Every save that adds, deletes or moves a row — changes its group, its place in the order, or whether it is
///         counted — numbers the rows of each group it touched anew, from the earliest place it touched on, before the
///         save's transaction commits: 1, 2, 3 … in the order <c>by</c> says, a row <c>skip</c> leaves out getting none.
///         A group is numbered by one save at a time on every database (a transaction-scoped lock per group on SQL
///         Server and PostgreSQL; SQLite has one writer), and the rows it holds in memory are told their new numbers.
///     </para>
///     <para>
///         <c>by</c> is a member of the row, or several as an anonymous type: plain values or one-value value objects, compared by
///         their value. The number is a whole number, nullable when a row can have none, or a one-value value object over
///         one. A row whose group is null is not numbered.
///     </para>
/// </remarks>
public static class NumberedBuilderExtensions
{
    /// <summary>Makes <paramref name="number" /> the row's place among its group's rows, kept by Rask.</summary>
    /// <typeparam name="TEntity">The numbered entity.</typeparam>
    /// <typeparam name="TNumber">The number's type: <c>int</c>, <c>int?</c>, <c>long</c>, or a one-value value object.</typeparam>
    /// <typeparam name="TWithin">The group's type.</typeparam>
    /// <typeparam name="TOrder">The order: one member, or several as an anonymous type.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="number">The numbered property: <c>f =&gt; f.Sequence</c>.</param>
    /// <param name="within">What the rows are counted within: <c>f =&gt; f.CountedAt</c>.</param>
    /// <param name="by">Their order within it, the first member first: <c>f =&gt; new { f.Moment, f.Number, f.Id }</c>.</param>
    /// <param name="skip">The rows that are not counted and have no number: <c>f =&gt; f.MergedInto != null</c>.</param>
    /// <returns>The numbering, to say what more decides it: <see cref="Numbering{TEntity}.Same{TRow}" />, <see cref="Numbering{TEntity}.SkipWhen{TOther, TRow}" />.</returns>
    /// <exception cref="ArgumentException">A member is not one of the entity's own, or the entity's key is not one column.</exception>
    public static Numbering<TEntity> Numbered<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TNumber, TWithin, TOrder>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TNumber>> number,
        Expression<Func<TEntity, TWithin>> within,
        Expression<Func<TEntity, TOrder>> by,
        Expression<Func<TEntity, bool>>? skip = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(number);
        ArgumentNullException.ThrowIfNull(within);
        ArgumentNullException.ThrowIfNull(by);

        var name = number.Body is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException($"Numbered({number}) must name a property of {typeof(TEntity).Name}: f => f.Sequence.", nameof(number));
        if (entity.Metadata.FindPrimaryKey() is { Properties.Count: > 1 })
        {
            throw new ArgumentException($"Numbered({number}) numbers rows by their key, and {typeof(TEntity).Name}'s is several columns.", nameof(number));
        }

        var declared = new Numbering<TEntity>(
            name, number, within, NumberingOrder.Members(by), skip,
            (rows, value, cancellationToken) =>
                rows.ExecuteUpdateAsync(set => set.SetProperty(number, value is null ? default! : (TNumber)value), cancellationToken));
        entity.Property(number).ValueGeneratedNever();
        Numberings.Declare(declared);
        return declared;
    }
}
