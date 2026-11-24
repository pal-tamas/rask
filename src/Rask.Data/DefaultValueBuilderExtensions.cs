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
///         Each is written in the provider's own SQL as the model is finalized — <c>HasDefaultValueSql("1000")</c>, exactly
///         as if the model had said it — so a model moving from <c>HasDefaultValueSql</c> to <c>Default</c> changes no
///         column.
///     </para>
///     <para>
///         <c>() =&gt; Clock.Now</c> is the database's clock, in UTC: <c>SYSUTCDATETIME()</c> on SQL Server,
///         <c>now()</c> on PostgreSQL, <c>strftime(…, 'now')</c> on SQLite. It is the moment the database writes the
///         row, read on the database server — not <see cref="Clock" />, and a test's <c>Clock.Fake</c> does not move
///         it. A row Rask writes takes its moment from the entity, which reads <c>Clock.Now</c> (an aggregate's
///         <c>Timestamps</c> are stamped so). <c>() =&gt; Clock.Wall</c> is the database's wall clock:
///         <c>CURRENT_TIMESTAMP</c>, <c>LOCALTIMESTAMP</c>, SQLite's <c>'localtime'</c> — the local time of the zone the
///         database server runs in, which is the app's only when the server runs in <see cref="Clock.Zone" />.
///     </para>
/// </remarks>
public static class DefaultValueBuilderExtensions
{
    /// <summary>Gives <paramref name="property" /> the database default <paramref name="value" />.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">The property's type.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property: <c>x =&gt; x.Size</c>.</param>
    /// <param name="value">
    ///     The default, written as the column stores it — an enum's number, a strongly-typed id's key, a value object's
    ///     value. A value the value object refuses goes as the column stores it, with <c>StoredDefault</c>.
    /// </param>
    /// <returns>The property's builder.</returns>
    public static PropertyBuilder<TProperty> Default<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        TProperty value)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);

        return Declare(entity, property, new DeclaredDefault(DefaultKind.Value, value));
    }

    /// <summary>Gives <paramref name="property" /> the database's clock as its default.</summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">
    ///     The property's type: a <see cref="DateTimeOffset" />, a <see cref="DateTime" />, or a value object over one.
    /// </typeparam>
    /// <typeparam name="TClock">The clock's type: <see cref="DateTimeOffset" /> or <see cref="DateTime" />.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property: <c>x =&gt; x.CreatedAt</c>.</param>
    /// <param name="now">
    ///     <c>() =&gt; Clock.Now</c> (or <c>() =&gt; Clock.Now.UtcDateTime</c> for a <see cref="DateTime" />), the
    ///     database's clock in UTC; or <c>() =&gt; Clock.Wall</c>, its wall clock — the local time of the zone the
    ///     database server runs in.
    /// </param>
    /// <returns>The property's builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="now" /> is anything but one of those.</exception>
    public static PropertyBuilder<TProperty> Default<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty, TClock>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        Expression<Func<TClock>> now)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(now);

        return Declare(entity, property, DatabaseDefaultConvention.Clock(now.Body, $"Default({property}, {now})"));
    }

    /// <summary>
    ///     Gives <paramref name="property" /> the database default <paramref name="value" />, as the column stores it:
    ///     <c>b.StoredDefault(p =&gt; p.HardwareNumber, 0)</c> — a default the value object would refuse, such as the
    ///     <c>0</c> a legacy schema gives a <c>HardwareNumber</c> that must be at least 1.
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TProperty">The property's type: a value object, an enum, a strongly-typed id.</typeparam>
    /// <typeparam name="TStored">The type the column stores: the value object's value, the enum's number.</typeparam>
    /// <param name="entity">The entity's builder.</param>
    /// <param name="property">The property: <c>x =&gt; x.HardwareNumber</c>.</param>
    /// <param name="value">The default, as the column stores it.</param>
    /// <returns>The property's builder.</returns>
    /// <remarks>
    ///     The type is checked as the model is built: a <c>HardwareNumber(int Value)</c> takes an <see cref="int" />, and
    ///     anything else fails the build with the type the column stores.
    /// </remarks>
    public static PropertyBuilder<TProperty> StoredDefault<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty, TStored>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TProperty>> property,
        TStored value)
        where TEntity : class
        where TStored : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);

        return Declare(entity, property, new DeclaredDefault(DefaultKind.Stored, value));
    }

    private static PropertyBuilder<TProperty> Declare<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TProperty>(
        EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, TProperty>> property, DeclaredDefault declared)
        where TEntity : class
    {
        var builder = entity.Property(property).ValueGeneratedNever();
        builder.Metadata.SetAnnotation(DatabaseDefaultConvention.Annotation, declared);
        return builder;
    }
}
