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
    public static ModelBuilder ApplyRaskConventions(this ModelBuilder modelBuilder) =>
        Apply(modelBuilder, context: null);

    /// <summary>
    /// Applies Rask.Data's model conventions, including the tenant filter, which needs the context.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">The context being built — pass <c>this</c> from <c>OnModelCreating</c>.</param>
    /// <returns>The same model builder.</returns>
    /// <remarks>
    /// <para>
    /// The tenant filter has to reach the current tenant through an instance member of the context, which is
    /// why this overload exists. A query filter is compiled into the model and the model is CACHED, so a
    /// static read is evaluated once and inlined into the SQL as a literal — the first tenant to run a query
    /// then pins that value for every tenant after it. Reaching the same value through the context instance
    /// makes EF Core lift it to a real parameter and re-bind it per query.
    /// </para>
    /// <para>
    /// <paramref name="context" /> must implement <see cref="ITenantScoped" /> once anything is
    /// <see cref="Tenancy.PerTenant" />; the parameterless overload refuses rather than mapping a table whose
    /// filter could not be built.
    /// </para>
    /// </remarks>
    public static ModelBuilder ApplyRaskConventions(this ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Apply(modelBuilder, context);
    }

    private static ModelBuilder Apply(ModelBuilder modelBuilder, DbContext? context)
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
                // Both, unless the entity declared a `Stamps` const saying otherwise — an append-only table
                // has nothing an UpdatedAt could mean. Read from the registry the generator filled at
                // compile time, never reflected over: see TimestampRegistry.
                var stamps = ConventionRegistry.StampsFor(clrType);

                // Ignore, not merely "do not add": both are real properties on Entity<TId>, so EF Core's own
                // convention maps them whatever this does. Declining one has to say so out loud.
                if ((stamps & Timestamps.Created) != 0)
                {
                    builder.Property(typeof(DateTime), Columns.CreatedAt);
                }
                else
                {
                    builder.Ignore(Columns.CreatedAt);
                }

                if ((stamps & Timestamps.Updated) != 0)
                {
                    builder.Property(typeof(DateTime), Columns.UpdatedAt);
                }
                else
                {
                    builder.Ignore(Columns.UpdatedAt);
                }
            }

            // On unless the aggregate declined: a lost update is invisible, which is why this default is
            // not the one soft delete got.
            if (versioned)
            {
                if (ConventionRegistry.ChecksFor(clrType) == Concurrency.Version)
                {
                    builder.Property(typeof(int), Columns.Version).IsConcurrencyToken();
                }
                else
                {
                    builder.Ignore(Columns.Version);
                }
            }

            // OFF unless the aggregate asked. A stamped row still occupies its UNIQUE constraints, "delete my
            // account" has to be able to mean delete, and Rask already ships snapshots and Litestream for
            // getting data back — so keeping the row is a choice an aggregate makes, not one it inherits.
            if (softDeletable)
            {
                // Ignore, not merely "do not add" — the same rule as the timestamps above. Version and
                // DeletedAt are real properties on Aggregate<TId>, so EF Core maps them by its own convention
                // whatever this does; declining one has to say so out loud.
                if (ConventionRegistry.DeletesFor(clrType) == Deletion.Soft)
                {
                    builder.Property(typeof(DateTime?), Columns.DeletedAt);

                    // NAMED, so IgnoreQueryFilters() can lift this one and leave the tenant filter standing.
                    builder.HasQueryFilter(SoftDeleteFilter, BuildNotDeletedFilter(builder, clrType));
                }
                else
                {
                    builder.Ignore(Columns.DeletedAt);
                }
            }
        }

        MapValueCollections(modelBuilder, clrTypes);
        ApplyTenancy(modelBuilder, clrTypes, context);
        BindChildrenToTheirParents(modelBuilder);

        return modelBuilder;
    }

    // EF.Property<Guid?>(entity, "TenantId"), captured from a real expression rather than through
    // MakeGenericMethod — no reflection for the trimmer to be unable to follow, as with the DeletedAt one.
    private static readonly MethodInfo EfPropertyNullableGuid =
        ((MethodCallExpression)((Expression<Func<object, Guid?>>)(e => EF.Property<Guid?>(e, Columns.TenantId))).Body)
        .Method;

    /// <summary>The name of the soft-delete query filter, which <c>IgnoreQueryFilters()</c> lifts.</summary>
    internal const string SoftDeleteFilter = "SoftDelete";

    /// <summary>The name of the tenant query filter, which nothing lifts except <see cref="Tenant.Across" />.</summary>
    internal const string TenantFilter = "Tenant";

    /// <summary>
    /// Gives every <see cref="Tenancy.PerTenant" /> table its <c>TenantId</c> column, its query filter and a
    /// <c>TenantId</c> prefix on each of its indexes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index prefix is not a nicety. <c>HasIndex(p =&gt; p.Sku).IsUnique()</c> on a partitioned table
    /// otherwise means "no two tenants may ever use the same SKU", and the symptom is one tenant unable to
    /// create a row because a different tenant already has it — with nothing in the code saying so.
    /// </para>
    /// <para>
    /// <c>TenantId</c> is ignored on a table that did not ask, exactly as <c>DeletedAt</c> and
    /// <c>Version</c> are: it is a real property on <see cref="Entity{TId}" /> so a child carries it too, and
    /// EF Core maps a real property by its own convention whatever this does.
    /// </para>
    /// </remarks>
    private static void ApplyTenancy(ModelBuilder modelBuilder, List<Type> clrTypes, DbContext? context)
    {
        foreach (var clrType in clrTypes)
        {
            if (!typeof(IEntity).IsAssignableFrom(clrType))
            {
                continue;
            }

            var builder = modelBuilder.Entity(clrType);

            if (ConventionRegistry.ScopeFor(clrType) != Tenancy.PerTenant)
            {
                // Unless the entity mapped it ITSELF. Rask.Auth does: its accounts table carries an optional
                // tenant that it manages, because a tenant-scoped row is stamped from the ambient tenant and
                // refused without one, and an administrator legitimately has none. Ignoring it here would
                // silently undo that — the same rule as every other convention in this file.
                if (((IConventionEntityType)builder.Metadata).FindProperty(Columns.TenantId)
                    ?.GetConfigurationSource() is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation))
                {
                    builder.Ignore(Columns.TenantId);
                }

                continue;
            }

            if (context is not ITenantScoped)
            {
                throw new InvalidOperationException(
                    $"'{clrType.Name}' declares Scope = Tenancy.PerTenant, but its DbContext " +
                    $"('{context?.GetType().Name ?? "none"}') cannot supply the current tenant. Declare the " +
                    "context as ': DbContext, ITenantScoped' and call " +
                    "modelBuilder.ApplyRaskConventions(this) — the filter has to read the tenant through the " +
                    "context, or EF Core inlines one tenant's id into the cached query for every tenant.");
            }

            builder.Property(typeof(Guid?), Columns.TenantId);
            builder.HasQueryFilter(TenantFilter, BuildTenantFilter(clrType, context));

            PrefixIndexesWithTenant(builder);
        }
    }

    // e => current == null || EF.Property<Guid?>(e, "TenantId") == current
    //
    // The `current == null` arm is what makes Tenant.Across() mean "every tenant" rather than "the rows
    // nobody owns". Without it a null ambient narrows to TenantId IS NULL, which returns nothing on a table
    // where every row is owned — a cross-tenant admin view that is silently, plausibly empty.
    internal static LambdaExpression BuildTenantFilter(Type clrType, DbContext context)
    {
        var entity = Expression.Parameter(clrType, "e");

        var stored = Expression.Call(EfPropertyNullableGuid, entity, Expression.Constant(Columns.TenantId));

        var current = Expression.Property(
            Expression.Convert(Expression.Constant(context), typeof(ITenantScoped)),
            nameof(ITenantScoped.CurrentTenant));

        var unrestricted = Expression.Equal(current, Expression.Constant(null, typeof(Guid?)));

        return Expression.Lambda(
            Expression.OrElse(unrestricted, Expression.Equal(stored, current)), entity);
    }

    // Every index gains TenantId at the FRONT: uniqueness then means "within this tenant", and the filtered
    // query can use the index rather than scanning and discarding.
    private static void PrefixIndexesWithTenant(EntityTypeBuilder builder)
    {
        foreach (var index in builder.Metadata.GetIndexes().ToList())
        {
            var names = index.Properties.Select(static p => p.Name).ToList();

            if (names.Contains(Columns.TenantId, StringComparer.Ordinal))
            {
                continue;
            }

            var replacement = builder.HasIndex([Columns.TenantId, .. names]);

            if (index.IsUnique)
            {
                replacement.IsUnique();
            }

            builder.Metadata.RemoveIndex(index.Properties);
        }
    }

    /// <summary>
    /// Maps every collection of values an entity holds: a JSON column for value objects, a primitive
    /// collection for plain ones.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, both shapes fail — and neither fails loudly. A collection of value objects makes EF Core
    /// take the element for an entity type and refuse the whole model with <em>"requires a primary key to be
    /// defined"</em>, which is advice a value object must not take. A collection of plain values behind the
    /// read-only view Rask recommends everywhere else — <c>IReadOnlyList&lt;string&gt; Tags =&gt; _tags</c> — is
    /// not mapped at all: the build is green, no diagnostic fires, and what a domain method added is simply
    /// gone on the next read.
    /// </para>
    /// <para>
    /// Both are replaced wholesale, which is what a value is: there are no keys, no identity and nothing to
    /// reconcile row by row. A collection that needs its own identity is a collection of
    /// <see cref="Entity{TId}" /> children instead, and keeps its own table.
    /// </para>
    /// <para>
    /// What the entity's own <c>Configure</c> already mapped is left exactly as it is — this runs last, so
    /// mapping over it would silently undo the author's own answer.
    /// </para>
    /// </remarks>
    private static void MapValueCollections(ModelBuilder modelBuilder, List<Type> clrTypes)
    {
        foreach (var clrType in clrTypes)
        {
            var declared = ConventionRegistry.CollectionsFor(clrType);
            if (declared.Count == 0)
            {
                continue;
            }

            var builder = modelBuilder.Entity(clrType);

            var conventional = (IConventionEntityType)builder.Metadata;

            foreach (var collection in declared)
            {
                // What the author configured stays exactly as it is — but EF Core's OWN convention has
                // already been here, and for a collection of value objects it has already made the element a
                // navigation to an entity type. Only an explicit answer counts as "said otherwise"; a
                // conventional one is the thing being corrected.
                if (IsDeclared(conventional.FindProperty(collection.Property)?.GetConfigurationSource()) ||
                    IsDeclared(conventional.FindNavigation(collection.Property)?.GetConfigurationSource()))
                {
                    continue;
                }

                if (collection.Element is { } element)
                {
                    builder.OwnsMany(element, collection.Property, owned => owned.ToJson());

                    if (collection.Field is { } ownedField)
                    {
                        builder.Navigation(collection.Property).HasField(ownedField);
                    }

                    continue;
                }

                var primitive = builder.PrimitiveCollection(collection.Property);

                if (collection.Field is { } field)
                {
                    primitive.HasField(field);
                }
            }
        }

        static bool IsDeclared(ConfigurationSource? source) =>
            source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation;
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
    internal static LambdaExpression BuildNotDeletedFilter(EntityTypeBuilder builder, Type clrType)
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
