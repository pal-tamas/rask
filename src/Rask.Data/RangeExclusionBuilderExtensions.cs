using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
/// Declares a non-overlapping range rule on an entity. PostgreSQL spells this as an exclusion constraint and
/// SQLite has no equivalent at all, so Rask carries the rule as model metadata and lets the provider emit
/// whatever enforces it.
/// </summary>
public static class RangeExclusionBuilderExtensions
{
    /// <summary>
    /// Declares that no two rows of <typeparamref name="TEntity"/> may cover the same point of the half-open
    /// range <c>[lo, hi)</c>, optionally scoped to a partition.
    /// </summary>
    /// <remarks>
    /// The bounds must be a type the store orders correctly — a date, a number, or a <c>yyyy-MM-dd</c> string.
    /// Because the range is half-open, <c>[100, 200)</c> and <c>[200, 300)</c> are neighbours rather than a
    /// conflict. Pair this with a check constraint (or a domain invariant) keeping <c>lo &lt; hi</c>: the rule
    /// assumes well-formed ranges and says nothing about inverted ones.
    /// <para>
    /// Enforcement lives in the database, so the rule also holds against writes that never went through this
    /// <c>DbContext</c>. It is emitted by migrations — an existing table only gains it from a new migration,
    /// and a database created with <c>EnsureCreated</c> does not get it at all.
    /// </para>
    /// </remarks>
    /// <typeparam name="TEntity">The entity declaring the rule.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="lo">The inclusive lower bound property, e.g. <c>x =&gt; x.StartsAt</c>.</param>
    /// <param name="hi">The exclusive upper bound property, e.g. <c>x =&gt; x.EndsAt</c>.</param>
    /// <param name="partitionBy">
    /// Properties scoping the rule — <c>x =&gt; x.RoomId</c>, or <c>x =&gt; new { x.Sku, x.Region }</c>. Omit
    /// to make the rule table-wide.
    /// </param>
    /// <param name="ignoreSoftDeleted">
    /// Excludes soft-deleted rows, so a deleted row frees its slot. Defaults to <see langword="true"/> when
    /// <typeparamref name="TEntity"/> declares <c>public const Deletion Deletes = Deletion.Soft;</c>, and to
    /// <see langword="false"/> otherwise — a hard-deleted row has already freed its slot, and there is no
    /// <c>DeletedAt</c> column to filter on.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An expression does not name plain properties of the entity, or <paramref name="ignoreSoftDeleted"/> is
    /// <see langword="true"/> for an entity that does not soft delete.
    /// </exception>
    public static EntityTypeBuilder<TEntity> HasNonOverlappingRange<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> lo,
        Expression<Func<TEntity, object?>> hi,
        Expression<Func<TEntity, object?>>? partitionBy = null,
        bool? ignoreSoftDeleted = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(lo);
        ArgumentNullException.ThrowIfNull(hi);

        // The same source ApplyRaskConventions maps DeletedAt from — being an aggregate no longer means having
        // the column, since soft delete is opt-in (#1131). The registry is filled by the generated module
        // initializer, so it is complete before any OnModelCreating runs.
        var softDeletable = ConventionRegistry.DeletesFor(typeof(TEntity)) == Deletion.Soft;

        if (ignoreSoftDeleted == true && !softDeletable)
        {
            throw new ArgumentException(
                $"'{typeof(TEntity).Name}' does not soft delete, so it has no DeletedAt column for a " +
                "non-overlapping range to ignore. Declare 'public const Deletion Deletes = Deletion.Soft;' on it, " +
                "or drop 'ignoreSoftDeleted: true' — a hard-deleted row has already freed its slot.",
                nameof(ignoreSoftDeleted));
        }

        var spec = new RangeExclusionSpec(
            PropertyExpressions.Single(lo, nameof(lo)),
            PropertyExpressions.Single(hi, nameof(hi)),
            partitionBy is null ? [] : PropertyExpressions.Many(partitionBy, nameof(partitionBy)),
            softDeletable && (ignoreSoftDeleted ?? true));

        builder.HasAnnotation(RangeExclusionSpec.AnnotationName, spec.Serialize());
        return builder;
    }
}
