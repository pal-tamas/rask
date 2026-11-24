using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     A column the database computes from the row's others, declared once in C# for every database:
///     <c>b.Computed(f =&gt; f.CountedAt, f =&gt; f.ModifiedPistol ?? f.Pistol)</c>.
/// </summary>
/// <remarks>
///     <para>
///         The value is written as the provider's own SQL as the model is finalized — a stored computed column on SQL
///         Server (<c>PERSISTED</c>), a stored generated column on PostgreSQL and on SQLite — so the database keeps it
///         with the row, it can be indexed, and EF Core reads it back after every save.
///     </para>
///     <para>
///         The value takes the subset that means the same on every database as it does in C#: the entity's mapped
///         members (a value object's too), constants and captured fields, <c>??</c>, <c>+ - * / %</c> on whole numbers
///         and doubles, text joined with <c>+</c> (a whole number joined as C# writes it, a null as no text),
///         comparisons, <c>&amp;&amp;</c>, <c>||</c>, <c>!</c>, <c>?:</c>, <c>HasValue</c>, <c>GetValueOrDefault</c>
///         and <c>Math.Abs</c>. Anything else — a method call, a <c>decimal</c> computed on, <c>.Value</c> — fails
///         the model build, naming what it met.
///     </para>
/// </remarks>
public static class ComputedColumnBuilderExtensions
{
    /// <summary>Makes <paramref name="property" /> a column the database computes as <paramref name="value" /> says.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">The property's type.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property the database computes: <c>f =&gt; f.CountedAt</c>.</param>
    /// <param name="value">What it is, from the row: <c>f =&gt; f.ModifiedPistol ?? f.Pistol</c>.</param>
    /// <returns>The property's builder.</returns>
    public static PropertyBuilder<TProperty> Computed<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TEntity, TProperty>> value)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        var builder = entity.Property(property);
        builder.Metadata.SetAnnotation(
            ComputedColumnConvention.Annotation, new DeclaredExpression(value, $"Computed({property}, {value})"));
        builder.ValueGeneratedOnAddOrUpdate();
        return builder;
    }
}
