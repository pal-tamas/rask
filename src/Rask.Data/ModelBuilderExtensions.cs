using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    /// <see cref="PerTenant" />; the parameterless overload refuses rather than mapping a table whose
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
        // its entity types is what makes a complex type get promoted to one. The entity types, not their CLR types:
        // EF annotates ClrType with what Entity(Type) needs kept by the trimmer, and a List<Type> would drop it.
        var entityTypes = modelBuilder.Model.GetEntityTypes().ToList();

#pragma warning disable S3267 // ClrType's trimmer annotation survives a local, not a Select projection
        foreach (var entityType in entityTypes)
        {
            ApplyEntityConventions(modelBuilder, entityType.ClrType);
        }
#pragma warning restore S3267

        UseChildFields(entityTypes);
        IgnoreComputedProperties(modelBuilder, entityTypes);
        MapValueCollections(modelBuilder, entityTypes);
        ApplyTenancy(modelBuilder, entityTypes, context);
        TenantColumnFollowers.Settle(modelBuilder);
        BindChildrenToTheirParents(modelBuilder);

        return modelBuilder;
    }

    // Children behind a field EF Core's convention cannot pair with the navigation by name — `_items` behind `Lines`,
    // handed out as `_items.AsReadOnly()` — would be loaded into the read-only view, which fails. The generator found
    // the field, on the entity or a base, so it is set here; a field the entity's own Configure chose stays.
    private static void UseChildFields(List<IMutableEntityType> entityTypes)
    {
#pragma warning disable S3267 // ClrType's trimmer annotation survives a local, not a Select projection
        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;

            foreach (var child in ConventionRegistry.ChildFieldsFor(clrType))
            {
                if (entityType.FindNavigation(child.Navigation) is { } navigation &&
                    ((IConventionNavigation)navigation).GetFieldInfoConfigurationSource() is not
                        (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation))
                {
                    UseField(navigation, clrType, child.Field);
                }
            }
        }
#pragma warning restore S3267
    }

    // `public SupplyKind Kind => …;` is a value computed from other columns, not a column of its own. A scalar
    // like it EF Core leaves alone, but a struct or record it takes for a complex property — and then fails at
    // startup because there is no field to set. A property with a setter, or the compiler's own backing field
    // (`{ get; }`), is stored; one with neither can only be computed.
    private static void IgnoreComputedProperties(ModelBuilder modelBuilder, List<IMutableEntityType> entityTypes)
    {
#pragma warning disable S3267 // ClrType's trimmer annotation survives a local, not a Select projection
        foreach (var entityType in entityTypes)
        {
            foreach (var complex in entityType.GetComplexProperties().ToList())
            {
                if (complex.FieldInfo is null && complex.PropertyInfo is { SetMethod: null })
                {
                    modelBuilder.Entity(entityType.ClrType).Ignore(complex.Name);
                }
            }

            foreach (var view in ComputedViews(entityType))
            {
                modelBuilder.Entity(entityType.ClrType).Ignore(view);
            }
        }
#pragma warning restore S3267
    }

    // `HeavyItems => _items.Where(…)` beside `Items => _items`: EF Core takes the view for a second relationship to the
    // same children, and fails the first save ("does not implement ICollection<>"). A collection navigation with no
    // setter and no field, beside one to the same entity that has either, is computed from that one — the rule the
    // generators leave it off the form model and the read face by.
    private static List<string> ComputedViews(IMutableEntityType entityType)
    {
        var collections = entityType.GetDeclaredNavigations().Where(static n => n.IsCollection).ToList();

        return [.. collections
            .Where(view => IsView(view) && collections.Exists(stored =>
                !IsView(stored) && ReferenceEquals(stored.TargetEntityType, view.TargetEntityType)))
            .Select(static view => view.Name)];

        static bool IsView(IMutableNavigation navigation) =>
            navigation.FieldInfo is null && navigation.PropertyInfo is { SetMethod: null };
    }

    private static void ApplyEntityConventions(
        ModelBuilder modelBuilder, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType)
    {
        ApplyKeyConvention(modelBuilder, clrType);

        var timestamped = typeof(IEntity).IsAssignableFrom(clrType);
        var aggregate = typeof(IAggregate).IsAssignableFrom(clrType);
        if (!timestamped && !aggregate)
        {
            return;
        }

        var builder = modelBuilder.Entity(clrType);

        if (timestamped)
        {
            ApplyTimestamps(builder, clrType);
        }

        if (aggregate)
        {
            ApplyVersion(builder, clrType);
            ApplySoftDelete(builder, clrType);
        }
    }

    private static void ApplyTimestamps(EntityTypeBuilder builder, Type clrType)
    {
        // Only what the base names — Aggregate<Guid, Timestamps> or Entity<Guid, Created> — read from the
        // registry the generator filled at compile time, never reflected over.
        //
        // Ignore, not merely "do not add": both are real properties on Entity<TId>, so EF Core's own
        // convention maps them whatever this does. Leaving one out has to say so out loud.
        ApplyStamp(builder, clrType, TraitSet.Created, Columns.CreatedAt);
        ApplyStamp(builder, clrType, TraitSet.Updated, Columns.UpdatedAt);
    }

    private static void ApplyStamp(EntityTypeBuilder builder, Type clrType, TraitSet trait, string column)
    {
        if (ConventionRegistry.Has(clrType, trait))
        {
            builder.Property<DateTime>(column);
        }
        else
        {
            builder.Ignore(column);
        }
    }

    // Only for an aggregate that names Versioned.
    private static void ApplyVersion(EntityTypeBuilder builder, Type clrType)
    {
        if (ConventionRegistry.Has(clrType, TraitSet.Versioned))
        {
            builder.Property<int>(Columns.Version).IsConcurrencyToken();
        }
        else
        {
            builder.Ignore(Columns.Version);
        }
    }

    // Only for an aggregate that names SoftDelete. A stamped row still occupies its UNIQUE constraints.
    private static void ApplySoftDelete(EntityTypeBuilder builder, Type clrType)
    {
        // Ignore, not merely "do not add" — the same rule as the timestamps above. Version and
        // DeletedAt are real properties on Aggregate<TId>, so EF Core maps them by its own convention
        // whatever this does; declining one has to say so out loud.
        if (ConventionRegistry.Has(clrType, TraitSet.SoftDelete))
        {
            builder.Property<DateTime?>(Columns.DeletedAt);

            // NAMED, so IgnoreQueryFilters() can lift this one and leave the tenant filter standing.
            builder.HasQueryFilter(SoftDeleteFilter, BuildNotDeletedFilter(builder, clrType));
        }
        else
        {
            builder.Ignore(Columns.DeletedAt);
        }
    }

    /// <summary>The name of the soft-delete query filter, which <c>IgnoreQueryFilters()</c> lifts.</summary>
    internal const string SoftDeleteFilter = "SoftDelete";

    /// <summary>The name of the tenant query filter, which nothing lifts except <see cref="Tenant.Across" />.</summary>
    internal const string TenantFilter = "Tenant";

    /// <summary>
    /// Gives every <see cref="PerTenant" /> table its <c>TenantId</c> column and its query filter. Its
    /// indexes are what the app wrote: one that holds within a tenant names <c>TenantId</c> itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The column is the entity's own <c>public Guid? TenantId { get; private set; }</c> when it declares one —
    /// or <c>int?</c> or <c>long?</c>, for a table whose tenants are numbered: the declared type is the
    /// column's, the filter's and the stamp's (<see cref="TenantColumn" />) — and a shadow <c>Guid?</c> when an
    /// aggregate does not. A CHILD that declares none has no column at all: it belongs to its tenant through its
    /// parent, and its filter asks the parent (<see cref="TenantParents" />). A table that did not ask is left
    /// alone: a <c>TenantId</c> it declares is an ordinary column of its own, mapped by EF Core's convention.
    /// </para>
    /// </remarks>
    private static void ApplyTenancy(ModelBuilder modelBuilder, List<IMutableEntityType> entityTypes, DbContext? context)
    {
        foreach (var entityType in entityTypes)
        {
            if (!TenantParents.IsPartitioned(entityType.ClrType))
            {
                continue;
            }

            if (context is not ITenantScoped)
            {
                throw new InvalidOperationException(
                    $"'{entityType.ClrType.Name}' names PerTenant, but its DbContext " +
                    $"('{context?.GetType().Name ?? "none"}') cannot supply the current tenant. Declare the " +
                    "context as ': DbContext, ITenantScoped' and call " +
                    "modelBuilder.ApplyRaskConventions(this) — the filter has to read the tenant through the " +
                    "context, or EF Core inlines one tenant's id into the cached query for every tenant.");
            }

            PartitionByTenant(modelBuilder, entityType, context);
        }
    }

    /// <summary>
    /// Settles once more each child partitioned through its parent, after every entity's own <c>Configure</c>
    /// ran: it may have mapped a <c>TenantId</c> on the child, or moved the foreign key the filter names.
    /// </summary>
    internal static void SettleTenancyThroughParents(ModelBuilder modelBuilder, DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(TenantParents.IsThroughParent).ToList())
        {
            PartitionByTenant(modelBuilder, entityType, context);
        }
    }

    // Only the root carries the tenant (#1251): a child with no TenantId of its own — at any depth — has no column
    // and a filter that asks its parent, along the path the aggregate's collections make. A root keeps the column it
    // declared (Guid?, int? or long?) or a shadow Guid?; a child that declares one keeps it too.
    private static void PartitionByTenant(ModelBuilder modelBuilder, IMutableEntityType entityType, DbContext context)
    {
        var clrType = entityType.ClrType;
        var builder = modelBuilder.Entity(clrType);

        if (!typeof(IAggregate).IsAssignableFrom(clrType) && entityType.FindProperty(Columns.TenantId) is null)
        {
            var parents = TenantParents.KeysOf(entityType, TenantParents.IsPartitioned);

            // Never left unfiltered: a child with no column and no way up to its root would read every tenant.
            if (parents.Count == 0)
            {
                throw new InvalidOperationException(
                    $"'{clrType.Name}' is part of a tenant-scoped aggregate, but the model has no relationship from " +
                    "it up to a tenant-scoped parent, so its rows cannot be told apart by tenant. Hold it in a " +
                    "collection of its aggregate (or of a child of it) — IReadOnlyList<" + clrType.Name + "> on the " +
                    "parent — and map that relationship in the same context.");
            }

            builder.HasAnnotation(TenantParents.Annotation, true);
            builder.HasQueryFilter(TenantFilter, TenantParents.BuildFilter(clrType, parents, context));
            return;
        }

        entityType.RemoveAnnotation(TenantParents.Annotation);

        var column = TenantColumn.TypeFor(entityType);

        builder.Property(column, Columns.TenantId);
        builder.HasQueryFilter(TenantFilter, TenantColumn.BuildFilter(clrType, column, context));
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
    private static void MapValueCollections(ModelBuilder modelBuilder, List<IMutableEntityType> entityTypes)
    {
#pragma warning disable S3267 // ClrType's trimmer annotation survives a local, not a Select projection
        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;
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
                        UseField(builder.Navigation(collection.Property).Metadata, clrType, ownedField);
                    }

                    continue;
                }

                var primitive = builder.PrimitiveCollection(collection.Property);

                if (collection.Field is { } field)
                {
                    UseField(primitive.Metadata, clrType, field);
                }
            }
        }
#pragma warning restore S3267

        static bool IsDeclared(ConfigurationSource? source) =>
            source is ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation;
    }

    // HasField(name) looks on the entity's own type only, so a field an abstract base declares — the base's
    // `_tags` behind its `Tags` — is found on that base and set as itself.
    private static void UseField(
        IMutablePropertyBase property, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType, string field) =>
        property.FieldInfo = DeclaredField(clrType, field) ?? throw new InvalidOperationException(
            $"The field '{field}' behind '{clrType.Name}.{property.Name}' was not found on it or on any of its bases.");

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The field was named by the generator from the entity's own declaration, and the bases are " +
                        "the ones EF Core's backing-field convention walks for the same entity under the same annotation.")]
    private static FieldInfo? DeclaredField([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType, string name)
    {
#pragma warning disable S3011 // the field behind a read-only view is private by design; EF Core writes it the same way
        const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
#pragma warning restore S3011

        for (var type = clrType; type is not null; type = type.BaseType)
        {
            if (type.GetField(name, Declared) is { } field)
            {
                return field;
            }
        }

        return null;
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

                // A child of TWO parents (a Chip under a Driver or a Vehicle) holds one of its keys and NULL in the
                // other, so neither can be required; a severed one is deleted at save instead (SeveredChildren).
                var required = ParentKeysOf(navigation.TargetEntityType) == 1;

                // An application that said otherwise keeps what it said: this is a convention, not a rule.
                if (foreignKey.IsRequired == required && foreignKey.DeleteBehavior == DeleteBehavior.Cascade)
                {
                    continue;
                }

                foreignKey.IsRequired = required;
                foreignKey.DeleteBehavior = DeleteBehavior.Cascade;
            }
        }
    }

    /// <summary>How many parents hold <paramref name="child" /> in a collection: one foreign key each.</summary>
    internal static int ParentKeysOf(IReadOnlyEntityType child) =>
        child.GetForeignKeys().Count(static key =>
            key.PrincipalToDependent is { IsCollection: true } &&
            typeof(IEntity).IsAssignableFrom(key.PrincipalEntityType.ClrType) &&
            !typeof(IAggregate).IsAssignableFrom(key.DeclaringEntityType.ClrType));

    // An Entity<TId> key is the entity's to set, except an integer one, which the store's identity produces —
    // and except where the application already said how its key is generated. An app with a context of its
    // own calls this AFTER its own configuration, so overwriting an explicit ValueGeneratedOnAdd or a database
    // default here would send an empty key straight past the default that was meant to fill it.
    private static void ApplyKeyConvention(
        ModelBuilder modelBuilder, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType)
    {
        if (IdTypeOf(clrType) is not { } idType || IsInteger(idType))
        {
            return;
        }

        if (IsComposite(idType))
        {
            ApplyCompositeKey(modelBuilder, clrType);
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

    // Entity<(DeliveryNote, Transaction)>: keyed by the parts the generated Id names, in its order, each the entity's
    // to set. The names are the generated Id's own tuple names — a context wired by hand gets the same key as the
    // generated registry's. The inherited Id — the tuple, and the generated one that reads it — is no column.
    private static void ApplyCompositeKey(
        ModelBuilder modelBuilder, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType)
    {
        var builder = modelBuilder.Entity(clrType);
        var conventional = (IConventionEntityType)builder.Metadata;

        if (SaidByApp(conventional.GetPrimaryKeyConfigurationSource()) ||
            KeyPartsOf(clrType) is not { Length: > 0 } parts)
        {
            return;
        }

        builder.Ignore(nameof(Entity<int>.Id));
        builder.HasKey(parts);

        foreach (var part in parts)
        {
            builder.Property(part).ValueGeneratedNever();
        }
    }

    /// <summary>The parts of a key of several columns, as the generated <c>Id</c> names them, or null.</summary>
    internal static string[]? KeyPartsOf([DynamicallyAccessedMembers(DataTrimming.Entity)] Type clrType) =>
        clrType.GetProperty(nameof(Entity<int>.Id), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames is { Count: > 0 } names &&
        names.All(static n => n is not null)
            ? [.. names.Select(static n => n!)]
            : null;

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

    /// <summary>Whether <paramref name="idType" /> is a key of several columns — <c>Entity&lt;(DeliveryNote, Transaction)&gt;</c>.</summary>
    internal static bool IsComposite(Type idType) => typeof(ITuple).IsAssignableFrom(idType);

    /// <summary>Whether <paramref name="clrType" /> is keyed by several columns.</summary>
    internal static bool HasCompositeKey(Type clrType) => IdTypeOf(clrType) is { } idType && IsComposite(idType);

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
        var mapped = builder.Metadata.FindProperty(Columns.DeletedAt);

        // The member EF mapped, not a lookup by name: Expression.Property(x, "name") reflects over a type the trimmer
        // cannot see, and the metadata already holds the PropertyInfo (or the backing field) the column reads.
        Expression deletedAt = mapped is null || mapped.IsShadowProperty()
            ? Expression.Call(EfPropertyNullableDateTime, parameter, Expression.Constant(Columns.DeletedAt))
            : Expression.MakeMemberAccess(parameter, (MemberInfo?)mapped.PropertyInfo ?? mapped.FieldInfo!);

        var body = Expression.Equal(deletedAt, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(body, parameter);
    }
}
