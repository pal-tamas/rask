using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     Declares that no two rows of an entity that share a key may cover the same moment: a chip held by one driver at a
///     time, a room booked once per hour. The database holds the rule on every provider, and a save that breaks it is
///     refused with the rule's message under the range's field.
/// </summary>
public static class NoOverlapBuilderExtensions
{
    /// <summary>
    ///     No two rows with the same <paramref name="key" /> may overlap in <paramref name="range" />, a <c>Range</c> value
    ///     object: <c>b.NoOverlap(x =&gt; new { x.TenantId, x.Code }, x =&gt; x.Period, Texts.ChipHeld)</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity the rule is declared on.</typeparam>
    /// <param name="builder">The entity's builder.</param>
    /// <param name="key">
    ///     What two rows must share to conflict — <c>x =&gt; x.RoomId</c>, or <c>x =&gt; new { x.TenantId, x.Code }</c>.
    ///     A row whose key holds a null conflicts with nothing.
    /// </param>
    /// <param name="range">The <c>Range</c> value object: its start is inclusive, its end exclusive, and no end is open.</param>
    /// <param name="message">What a refused row is told, under the range's field.</param>
    /// <returns>The rule, to narrow with <see cref="NoOverlapBuilder{TEntity}.Where" />.</returns>
    /// <remarks>
    ///     <para>
    ///         The range is half-open, <c>[start, end)</c>: a row ending at 10:00 and one starting at 10:00 are neighbours,
    ///         not a conflict, and an empty range (start = end) conflicts with nothing. A soft-deleted row frees its range.
    ///     </para>
    ///     <para>
    ///         PostgreSQL holds it as an exclusion constraint (<c>btree_gist</c>), SQL Server and SQLite as triggers — all
    ///         three written by the app's migrations, and all three answering alike, so a save the rule refuses is a
    ///         <c>RaskValidationException</c> carrying <paramref name="message" />. Two saves at the same moment cannot
    ///         both get through.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">An expression names anything but the entity's own properties.</exception>
    public static NoOverlapBuilder<TEntity> NoOverlap<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> key,
        Expression<Func<TEntity, object?>> range,
        string message)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(range);

        return Declare(builder, key, message, PropertyExpressions.Single(range, nameof(range)), from: null, to: null);
    }

    /// <summary>
    ///     No two rows with the same <paramref name="key" /> may overlap between <paramref name="from" /> and
    ///     <paramref name="to" />: <c>b.NoOverlap(x =&gt; x.RoomId, x =&gt; x.StartsAt, x =&gt; x.EndsAt, Texts.RoomTaken)</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity the rule is declared on.</typeparam>
    /// <param name="builder">The entity's builder.</param>
    /// <param name="key">What two rows must share to conflict; a row whose key holds a null conflicts with nothing.</param>
    /// <param name="from">The inclusive start.</param>
    /// <param name="to">The exclusive end; a row holding null here is open, without end.</param>
    /// <param name="message">What a refused row is told, under the field of <paramref name="from" />.</param>
    /// <returns>The rule, to narrow with <see cref="NoOverlapBuilder{TEntity}.Where" />.</returns>
    /// <remarks>
    ///     The same rule as the <c>Range</c> overload's, over two properties of the entity: see
    ///     <see cref="NoOverlap{TEntity}(EntityTypeBuilder{TEntity}, Expression{Func{TEntity, object}}, Expression{Func{TEntity, object}}, string)" />.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">An expression names anything but the entity's own properties.</exception>
    public static NoOverlapBuilder<TEntity> NoOverlap<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> key,
        Expression<Func<TEntity, object?>> from,
        Expression<Func<TEntity, object?>> to,
        string message)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return Declare(
            builder, key, message, range: null, PropertyExpressions.Single(from, nameof(from)), PropertyExpressions.Single(to, nameof(to)));
    }

    private static NoOverlapBuilder<TEntity> Declare<TEntity>(
        EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, object?>> key, string message, string? range, string? from, string? to)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var declaration = new NoOverlapDeclaration(PropertyExpressions.Many(key, nameof(key)), range, from, to, message);
        var declared = builder.Metadata.FindAnnotation(NoOverlapDeclaration.Annotation)?.Value as List<NoOverlapDeclaration> ?? [];
        declared.Add(declaration);
        builder.Metadata.SetAnnotation(NoOverlapDeclaration.Annotation, declared);
        return new NoOverlapBuilder<TEntity>(declaration);
    }
}
