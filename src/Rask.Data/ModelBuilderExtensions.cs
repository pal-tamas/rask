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
    /// <b>Every <see cref="Model{TId}" /> owns its identity.</b> Its <c>Id</c> is marked never generated, so the
    /// key a factory set is the key that is inserted — unless the key is an integer, which the store's identity
    /// column produces. EF Core would otherwise mark a <see cref="Guid" /> key generated on add, and read a key
    /// that is already set as a row that already exists: a child added to a loaded aggregate
    /// (<c>order.AddLine(…)</c>) would be saved as an UPDATE that matches nothing. A key the application
    /// already configured — <c>ValueGeneratedOnAdd()</c>, a database default, a value generator — is left as it
    /// was, and on the generated model an entity's own static <c>Configure</c> runs after this and can say
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

    // A Model<TId> key is the entity's to set, except an integer one, which the store's identity produces —
    // and except where the application already said how its key is generated. An app with a context of its
    // own calls this AFTER its own configuration, so overwriting an explicit ValueGeneratedOnAdd or a database
    // default here would send an empty key straight past the default that was meant to fill it.
    private static void ApplyKeyConvention(ModelBuilder modelBuilder, Type clrType)
    {
        if (IdTypeOf(clrType) is not { } idType || IsInteger(idType))
        {
            return;
        }

        var key = modelBuilder.Entity(clrType).Property(nameof(Model<int>.Id));
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

    /// <summary>The <c>TId</c> of the <see cref="Model{TId}" /> that <paramref name="clrType" /> derives from, or null.</summary>
    internal static Type? IdTypeOf(Type clrType)
    {
        for (var type = clrType.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Model<>))
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
