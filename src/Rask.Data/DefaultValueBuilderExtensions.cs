using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     What the database puts in a column a row is written without, declared once in C# for every database:
///     <c>b.Default(x =&gt; x.Size, 1000)</c>, <c>b.Default(x =&gt; x.CreatedAt, () =&gt; Clock.Now)</c>.
/// </summary>
/// <remarks>
///     <para>
///         A default is the DATABASE's: it fills the rows written by something other than Rask — another app, a
///         script — and the rows a migration finds when it adds the column. Rask itself always writes the value the
///         entity holds, so a <c>Size</c> of 0 is saved as 0 and never quietly turned into the default (EF Core's own
///         <c>HasDefaultValue</c> leaves a column holding its type's default to the database). An entity's own
///         starting value is its initializer's, or its factory's.
///     </para>
///     <para>
///         <c>() =&gt; Clock.Now</c> is the database's clock, in UTC: <c>SYSUTCDATETIME()</c> on SQL Server,
///         <c>now()</c> on PostgreSQL, <c>strftime(…, 'now')</c> on SQLite. It is the moment the database writes the
///         row, read on the database server — not <see cref="Clock" />, and a test's <c>Clock.Fake</c> does not move
///         it. A row Rask writes takes its moment from the entity, which reads <c>Clock.Now</c> (an aggregate's
///         <c>Timestamps</c> are stamped so).
///     </para>
/// </remarks>
public static class DefaultValueBuilderExtensions
{
    /// <summary>Gives <paramref name="property" /> the database default <paramref name="value" />.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">The property's type.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property: <c>x =&gt; x.Size</c>.</param>
    /// <param name="value">The default, written as the column stores it — an enum, a strongly-typed id, a date.</param>
    /// <returns>The property's builder.</returns>
    public static PropertyBuilder<TProperty> Default<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        return entity.Property(property).HasDefaultValue(value).ValueGeneratedNever();
    }

    /// <summary>Gives <paramref name="property" /> the database's clock as its default.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">The property's type: a <see cref="DateTimeOffset" />, or a <see cref="DateTime" /> in UTC.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property: <c>x =&gt; x.CreatedAt</c>.</param>
    /// <param name="now"><c>() =&gt; Clock.Now</c>, or <c>() =&gt; Clock.Now.UtcDateTime</c> for a <see cref="DateTime" />.</param>
    /// <returns>The property's builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="now" /> is anything but the clock's now.</exception>
    public static PropertyBuilder<TProperty> Default<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TProperty>> now)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(now);

        DatabaseNowConvention.Check(now.Body, $"Default({property}, {now})");
        var builder = entity.Property(property).ValueGeneratedNever();
        builder.Metadata.SetAnnotation(DatabaseNowConvention.Annotation, true);
        return builder;
    }
}
