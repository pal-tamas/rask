using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
/// Applies Rask.Data's model conventions in a single call from <c>OnModelCreating</c>.
/// </summary>
public static class ModelBuilderExtensions
{
    // EF.Property<DateTime?>(entity, name), captured from a real expression rather than through
    // MakeGenericMethod — no reflection for the trimmer to be unable to follow.
    private static readonly MethodInfo EfPropertyNullableDateTime =
        ((MethodCallExpression)((Expression<Func<object, DateTime?>>)(e => EF.Property<DateTime?>(e, Columns.DeletedAt))).Body)
        .Method;

    /// <summary>
    /// Gives every marked entity the columns its markers imply, and the behaviour that goes with them:
    /// <see cref="ITimestamped"/> gets <c>CreatedAt</c>/<c>UpdatedAt</c>, <see cref="ISoftDeletable"/>
    /// gets <c>DeletedAt</c> plus the global query filter that hides a deleted row, and
    /// <see cref="IVersioned"/> gets <c>Version</c> marked as the optimistic-concurrency token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The properties do not have to exist on the class.</b> A marker alone is enough — the column is
    /// added as an EF <em>shadow property</em>, so a domain model can carry audit stamps and soft delete
    /// without any of that infrastructure appearing in the type you wrote.
    /// </para>
    /// <para>
    /// Declaring one is how you opt into <em>reading</em> it: write
    /// <c>public DateTime CreatedAt { get; private set; }</c> and it becomes an ordinary mapped property
    /// you can select, filter and render. Either way the framework writes it through the change tracker,
    /// which is why a private setter is enough.
    /// </para>
    /// <para>
    /// <b><see cref="IVersioned"/> is the exception</b> and must declare <c>public int Version</c>:
    /// optimistic concurrency exists to round-trip the token through an edit form, and a value the
    /// application cannot read is one it cannot send back. A model that marks itself versioned without
    /// the property is refused here, by name, rather than failing later as an update that matched no row.
    /// </para>
    /// <para>
    /// Call after the entity type configurations are applied — they establish the entity types this walks.
    /// </para>
    /// </remarks>
    public static ModelBuilder ApplyRaskConventions(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Materialised first: the calls below add properties, and mutating the model while enumerating
        // its entity types is what makes a complex type get promoted to one.
        var clrTypes = modelBuilder.Model.GetEntityTypes().Select(static e => e.ClrType).ToList();

        foreach (var clrType in clrTypes)
        {
            var timestamped = typeof(ITimestamped).IsAssignableFrom(clrType);
            var versioned = typeof(IVersioned).IsAssignableFrom(clrType);
            var softDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

            if (!timestamped && !versioned && !softDeletable)
            {
                continue;
            }

            var builder = modelBuilder.Entity(clrType);

            if (timestamped)
            {
                builder.Property(typeof(DateTime), Columns.CreatedAt);
                builder.Property(typeof(DateTime), Columns.UpdatedAt);
            }

            if (versioned)
            {
                // The one marker that needs a real property. A shadow concurrency token cannot work:
                // its original value has to survive the round trip that optimistic concurrency is for —
                // load a row, render a form, post it back — and a value the application cannot read is a
                // value it cannot send back. EF also refuses the update outright, with a message about
                // rows affected that names neither the token nor the reason. So this is said here.
                if (builder.Metadata.FindProperty(Columns.Version) is not { } versionProperty || versionProperty.IsShadowProperty())
                {
                    throw new InvalidOperationException(
                        $"'{clrType.Name}' implements IVersioned but does not declare the Version property, and " +
                        "optimistic concurrency cannot work without one — the token has to be readable to be " +
                        "round-tripped through an edit form, and a shadow column is not. Add " +
                        "`public int Version { get; private set; }` to the model. (CreatedAt, UpdatedAt and " +
                        "DeletedAt have no such requirement: leave them off and they become shadow columns.)");
                }

                builder.Property(typeof(int), Columns.Version).IsConcurrencyToken();
            }

            if (softDeletable)
            {
                builder.Property(typeof(DateTime?), Columns.DeletedAt);
                builder.HasQueryFilter(BuildNotDeletedFilter(builder, clrType));
            }
        }

        return modelBuilder;
    }

    // `e => e.DeletedAt == null`, or `e => EF.Property<DateTime?>(e, "DeletedAt") == null` when the class
    // does not declare the property and the column is a shadow one. EF's non-generic HasQueryFilter takes
    // a LambdaExpression, so the typed lambda is synthesized here either way.
    private static LambdaExpression BuildNotDeletedFilter(EntityTypeBuilder builder, Type clrType)
    {
        var parameter = Expression.Parameter(clrType, "e");
        var isShadow = builder.Metadata.FindProperty(Columns.DeletedAt)?.IsShadowProperty() ?? true;

        Expression deletedAt = isShadow
            ? Expression.Call(EfPropertyNullableDateTime, parameter, Expression.Constant(Columns.DeletedAt))
            : Expression.Property(parameter, Columns.DeletedAt);

        var body = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(body, parameter);
    }
}
