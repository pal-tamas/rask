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
    /// <typeparamref name="TEntity"/> is <see cref="ISoftDeletable"/>, and is ignored when it is not.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An expression does not name plain properties of the entity.</exception>
    public static EntityTypeBuilder<TEntity> HasNonOverlappingRange<TEntity>(
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

        var softDeletable = typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity));

        var spec = new RangeExclusionSpec(
            Single(lo, nameof(lo)),
            Single(hi, nameof(hi)),
            partitionBy is null ? [] : Properties(partitionBy, nameof(partitionBy)),
            softDeletable && (ignoreSoftDeleted ?? true));

        builder.HasAnnotation(RangeExclusionSpec.AnnotationName, spec.Serialize());
        return builder;
    }

    private static string Single<TEntity>(Expression<Func<TEntity, object?>> expression, string parameter)
    {
        var names = Properties(expression, parameter);
        return names.Count == 1
            ? names[0]
            : throw new ArgumentException($"'{parameter}' must name exactly one property.", parameter);
    }

    // Mirrors the shapes EF Core itself accepts for HasIndex: x => x.A, x => (object)x.A, x => new { x.A, x.B }.
    private static IReadOnlyList<string> Properties<TEntity>(
        Expression<Func<TEntity, object?>> expression,
        string parameter)
    {
        var body = Unwrap(expression.Body);

        if (body is NewExpression anonymous)
        {
            return anonymous.Arguments.Count == 0
                ? throw new ArgumentException($"'{parameter}' must name at least one property.", parameter)
                : [.. anonymous.Arguments.Select(argument => Name(argument, expression, parameter))];
        }

        return [Name(body, expression, parameter)];
    }

    private static string Name<TEntity>(
        Expression node,
        Expression<Func<TEntity, object?>> expression,
        string parameter)
        => Unwrap(node) is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException(
                $"'{parameter}' must name properties of {typeof(TEntity).Name} directly, as in x => x.Property " +
                $"or x => new {{ x.A, x.B }}, but was '{expression}'.",
                parameter);

    // A value-type property is boxed by the Func<TEntity, object?> signature; see through that cast.
    private static Expression Unwrap(Expression node)
        => node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
            ? cast.Operand
            : node;
}
