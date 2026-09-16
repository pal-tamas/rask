using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
    /// Gives every entity the framework's columns and the behaviour that goes with them: every
    /// <see cref="Entity{TId}"/> gets <c>CreatedAt</c>/<c>UpdatedAt</c>, and every <see cref="Aggregate{TId}"/> also
    /// gets <c>Version</c> as its optimistic-concurrency token and <c>DeletedAt</c> plus the global query filter
    /// that hides a deleted row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The properties are declared on the base classes with private setters: readable, so a screen can show
    /// "added on" and an edit can carry its <c>Version</c>, and written only by the framework, through the
    /// change tracker.
    /// </para>
    /// <para>
    /// <b>Every <see cref="Aggregate{TId}" /> owns its identity.</b> Its <c>Id</c> is marked never generated, so the
    /// key a factory set is the key that is inserted — unless the key is an integer, which the store's identity
    /// column produces. EF Core would otherwise mark a <see cref="Guid" /> key generated on add, and read a key
    /// that is already set as a row that already exists: a child added to a loaded aggregate
    /// (<c>order.AddLine(…)</c>) would be saved as an UPDATE that matches nothing. A key the application
    /// already configured — <c>ValueGeneratedOnAdd()</c>, a database default, a value generator — is left as it
    /// was, and in the generated model builder an entity's own static <c>Configure</c> runs after this and can say
    /// otherwise. An entity added with its key still at the default is refused at save by
    /// <see cref="AuditingInterceptor" /> rather than inserted with an empty key.
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
            ApplyKeyConvention(modelBuilder, clrType);

            var timestamped = typeof(IEntity).IsAssignableFrom(clrType);
            var versioned = typeof(IAggregate).IsAssignableFrom(clrType);
            var softDeletable = typeof(IAggregate).IsAssignableFrom(clrType);

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
                builder.Property(typeof(int), Columns.Version).IsConcurrencyToken();
            }

            if (softDeletable)
            {
                builder.Property(typeof(DateTime?), Columns.DeletedAt);
                builder.HasQueryFilter(BuildNotDeletedFilter(builder, clrType));
            }
        }

        BindChildrenToTheirParents(modelBuilder);

        return modelBuilder;
    }

    /// <summary>
    /// Makes every child's relationship to its aggregate required, and its delete a cascade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A child exists only as part of its aggregate, so it must not be able to outlive it. EF Core's own
    /// convention makes a shadow foreign key <b>nullable</b>, which means severing a child from its parent's
    /// collection — <c>order.Remove(line)</c>, or a form post that no longer holds that row — sets the key to NULL
    /// and <b>leaves the row in the table</b>: invisible through the navigation, unreachable through the
    /// aggregate, and impossible to delete through it either. The table grows a tombstone on every removal.
    /// </para>
    /// <para>
    /// Required plus cascade is what turns that severing into a delete, and what makes deleting the parent take
    /// its children with it. Only collections of <see cref="Entity{TId}" /> are touched: a collection of
    /// <see cref="Aggregate{TId}" /> is a reference to somebody else's data (RASK087), and making that required
    /// would let one aggregate's delete cascade into another's.
    /// </para>
    /// </remarks>
    private static void BindChildrenToTheirParents(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var navigation in entityType.GetNavigations().ToList())
            {
                if (!navigation.IsCollection ||
                    !typeof(IEntity).IsAssignableFrom(navigation.TargetEntityType.ClrType) ||
                    typeof(IAggregate).IsAssignableFrom(navigation.TargetEntityType.ClrType))
                {
                    continue;
                }

                var foreignKey = navigation.ForeignKey;

                // An application that said otherwise keeps what it said: this is a convention, not a rule.
                if (foreignKey.IsRequired && foreignKey.DeleteBehavior == DeleteBehavior.Cascade)
                {
                    continue;
                }

                foreignKey.IsRequired = true;
                foreignKey.DeleteBehavior = DeleteBehavior.Cascade;
            }
        }
    }

    // An Entity<TId> key is the entity's to set, except an integer one, which the store's identity produces —
    // and except where the application already said how its key is generated. An app with a context of its
    // own calls this AFTER its own configuration, so overwriting an explicit ValueGeneratedOnAdd or a database
    // default here would send an empty key straight past the default that was meant to fill it.
    private static void ApplyKeyConvention(ModelBuilder modelBuilder, Type clrType)
    {
        if (IdTypeOf(clrType) is not { } idType || IsInteger(idType))
        {
            return;
        }

        var key = modelBuilder.Entity(clrType).Property(nameof(Entity<int>.Id));
        var property = (IConventionProperty)key.Metadata;

        // Asked by WHO configured each aspect, never by its value: EF Core's own conventions fill values in that
        // the application never set, and reading one of those as the app's decision leaves every key generated.
        if (SaidByApp(property.GetValueGeneratedConfigurationSource()) ||
            SaidByApp(property.GetValueGeneratorFactoryConfigurationSource()) ||
            SaidByApp(property.GetDefaultValueSqlConfigurationSource()) ||
            SaidByApp(property.GetDefaultValueConfigurationSource()))
        {
            return;
        }

        key.ValueGeneratedNever();
    }

    private static bool SaidByApp(ConfigurationSource? source) =>
        source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation;

    /// <summary>The <c>TId</c> of the <see cref="Aggregate{TId}" /> that <paramref name="clrType" /> derives from, or null.</summary>
    internal static Type? IdTypeOf(Type clrType)
    {
        for (var type = clrType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="type" /> is an integer key type the store's identity produces.</summary>
    internal static bool IsInteger(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(int) || underlying == typeof(long)
            || underlying == typeof(short) || underlying == typeof(byte)
            || underlying == typeof(uint) || underlying == typeof(ulong)
            || underlying == typeof(ushort) || underlying == typeof(sbyte);
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
