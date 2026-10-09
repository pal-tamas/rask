using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     The read faces the model is built from, contributed by each assembly's generated registry — the read
///     side's counterpart to <see cref="ModelRegistry" />.
/// </summary>
/// <remarks>
///     <para>
///         The generator emits the CLR <em>shape</em> of a read face; the <em>mapping</em> is mirrored from
///         the write model here, at runtime. That split is load-bearing. An entity's own static
///         <c>Configure</c> can ignore a property, rename its column, move the table or add a value
///         conversion, and none of it is visible to a generator that only sees symbols. So nothing is derived
///         twice: when this configures <c>OrderRead</c> it reads the built <see cref="IEntityType" /> for
///         <c>Order</c> and copies what EF actually decided. Conventions, <c>Configure</c>, <c>[Column]</c>
///         and <c>[NotMapped]</c> all come out right because none of them is interpreted a second time.
///     </para>
///     <para>
///         A read face member whose write property was ignored is <see cref="EntityTypeBuilder.Ignore" />d
///         rather than mapped to a column that is not there — present on the CLR type, absent from the query.
///     </para>
/// </remarks>
public static class ReadModelRegistry
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<ReadEntityMapping>> Contributions = new();

    /// <summary>Whether any assembly has contributed a read face.</summary>
    public static bool IsEmpty => Contributions.IsEmpty;

    /// <summary>Replaces <paramref name="group" />'s contribution. Called by generated code.</summary>
    /// <param name="group">The generated registry class, standing for its assembly.</param>
    /// <param name="entities">The assembly's read faces.</param>
    public static void Replace(Type group, IReadOnlyList<ReadEntityMapping> entities)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(entities);

        Contributions[group] = entities;
    }

    /// <summary>
    ///     Maps every read face, copying what <paramref name="writeModel" /> decided for the entity behind it.
    /// </summary>
    /// <param name="modelBuilder">The read context's builder.</param>
    /// <param name="writeModel">
    ///     The built write model to mirror, or null when there is none to read — in which case the
    ///     convention's own answer stands, which is the same answer for an app that configures nothing.
    /// </param>
    public static ModelBuilder Apply(ModelBuilder modelBuilder, IModel? writeModel) =>
        Apply(modelBuilder, writeModel, context: null);

    /// <summary>
    ///     Maps every read face, giving it the context the tenant filter reads through.
    /// </summary>
    /// <param name="modelBuilder">The read context's builder.</param>
    /// <param name="writeModel">The built write model to mirror, or null when there is none.</param>
    /// <param name="context">The read context being built, or null when nothing is tenant-scoped.</param>
    /// <returns>The same model builder.</returns>
    /// <remarks>
    ///     The read face carries the tenant filter as well as the write side, and for a better reason: a read
    ///     face is the ONLY way an app queries, so a face without it would be the leak itself.
    /// </remarks>
    public static ModelBuilder Apply(ModelBuilder modelBuilder, IModel? writeModel, DbContext? context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var all = Contributions.Values.SelectMany(static c => c).ToList();

        foreach (var mapping in all)
        {
            MapColumns(modelBuilder, mapping, writeModel?.FindEntityType(mapping.WriteType), context);
        }

        // Relationships last: both ends have to be entity types before one can point at the other.
        foreach (var mapping in all)
        {
            MapRelationships(modelBuilder, mapping, writeModel);
        }

        return modelBuilder;
    }

    private static void MapColumns(
        ModelBuilder modelBuilder, ReadEntityMapping mapping, IEntityType? write, DbContext? context)
    {
        var builder = modelBuilder.Entity(mapping.ReadType);

        // The table the write side lands on, because a column name is only meaningful against one. The
        // parameterless GetColumnName() gives a complex property its BARE name — `Amount`, not
        // `Price_Amount` — so two value objects holding an Amount would both claim the same column.
        var store = write is null ? null : StoreObjectIdentifier.Create(write, StoreObjectType.Table);

        builder.ToTable(write?.GetTableName() ?? mapping.TableName, write?.GetSchema());
        builder.HasKey(nameof(Entity<int>.Id));

        foreach (var column in mapping.Columns)
        {
            MapColumn(builder, column, write, store);
        }

        // One column each, so they are mapped the same way the write side maps them rather than as a
        // property per element. The kind is settled at compile time and carried here, for the same reason
        // the write side carries it: whether an element is a value object is the generator's rule to apply.
        foreach (var collection in mapping.ValueCollections)
        {
            if (collection.ValueObject is { } element)
            {
                builder.OwnsMany(element, collection.Member, owned => owned.ToJson());
                continue;
            }

            builder.PrimitiveCollection(collection.Member);
        }

        MapAnnotationsAndFilters(builder, mapping, write, context);
    }

    private static void MapColumn(
        EntityTypeBuilder builder, ReadColumnMapping column, IEntityType? write, StoreObjectIdentifier? store)
    {
        var source = write is null ? null : FindProperty(write, column.Path);

        // The write model knows this entity and does not have the property: it was ignored, renamed out
        // of existence, or is not stored at all. Mapping it anyway would ask for a column the table has
        // not got, and the first query would fail on something the author never wrote.
        if (write is not null && source is null)
        {
            builder.Ignore(column.Member);
            return;
        }

        var property = builder.Property(column.Member);

        if (source is null)
        {
            property.HasColumnName(column.ColumnName);
            return;
        }

        property.HasColumnName(
            (store is { } table ? source.GetColumnName(table) : source.GetColumnName()) ?? column.ColumnName);
        property.IsRequired(!source.IsNullable);

        if (source.GetColumnType() is { Length: > 0 } columnType)
        {
            property.HasColumnType(columnType);
        }

        if (source.GetValueConverter() is { } converter)
        {
            property.HasConversion(converter);
        }

        if (source.GetMaxLength() is { } maxLength)
        {
            property.HasMaxLength(maxLength);
        }

        if (source.IsUnicode() is { } unicode)
        {
            property.IsUnicode(unicode);
        }
    }

    // Rask's own annotations and the write side's query filters, mirrored onto the face.
    private static void MapAnnotationsAndFilters(
        EntityTypeBuilder builder, ReadEntityMapping mapping, IEntityType? write, DbContext? context)
    {
        // Rask's own annotations travel with the face. The full-text index is declared on the ENTITY —
        // builder.HasFullTextSearch(x => new { x.Title, x.Body }) — and Search is now a read-side operator,
        // so without this Post.Search would refuse with "no full-text index" while the table plainly
        // has one. Copied rather than re-derived, like every other part of the mapping.
        if (write?.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value is { } fullText)
        {
            builder.HasAnnotation(FullTextSearchSpec.AnnotationName, fullText);
        }

        // Only where there is something to filter on. An aggregate normally has DeletedAt from Rask's
        // conventions, but an app that maps its own context without them — or that ignored the column —
        // would otherwise get a query filter over a property this face does not map, and EF refuses the
        // model with an error naming neither the filter nor the reason.
        if (mapping.IsRoot && builder.Metadata.FindProperty(Columns.DeletedAt) is not null)
        {
            builder.HasQueryFilter(
                ModelBuilderExtensions.SoftDeleteFilter,
                ModelBuilderExtensions.BuildNotDeletedFilter(builder, mapping.ReadType));
        }

        // The write side's answer, mirrored like everything else here rather than re-derived: if the entity
        // behind this face is partitioned, so is the face — children included, which is why a child's own
        // read face is safe to query on its own.
        if (ConventionRegistry.ScopeFor(mapping.WriteType) == Tenancy.PerTenant)
        {
            if (context is not ITenantScoped)
            {
                throw new InvalidOperationException(
                    $"'{mapping.WriteType.Name}' is tenant-scoped, but the read context " +
                    $"('{context?.GetType().Name ?? "none"}') cannot supply the current tenant. It must be " +
                    "declared ': DbContext, ITenantScoped'.");
            }

            // In the write side's type, which is the column's: a face that compared an int column with a Guid
            // would match nothing on one provider and fail on another.
            var column = write is null ? TenantColumn.TypeFor(mapping.WriteType) : TenantColumn.TypeFor(write);

            builder.Property(column, Columns.TenantId);
            builder.HasQueryFilter(
                ModelBuilderExtensions.TenantFilter,
                TenantColumn.BuildFilter(mapping.ReadType, column, context));
        }
    }

    private static void MapRelationships(ModelBuilder modelBuilder, ReadEntityMapping mapping, IModel? writeModel)
    {
        var builder = modelBuilder.Entity(mapping.ReadType);

        foreach (var reference in mapping.References)
        {
            // Nothing is ever saved through the read context, so the delete behaviour is only there to stop
            // EF Core seeing a cycle it has to break. NoAction says "this join describes the data, it does
            // not govern it".
            builder
                .HasOne(reference.TargetReadType, reference.Navigation)
                .WithMany()
                .HasForeignKey(reference.ForeignKey)
                .OnDelete(DeleteBehavior.NoAction);
        }

        foreach (var child in mapping.Children)
        {
            modelBuilder
                .Entity(child.ChildReadType)
                .HasOne(mapping.ReadType, child.Inverse)
                .WithMany(child.Collection)
                .HasForeignKey(ChildForeignKey(writeModel, mapping.WriteType, child))
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    // The child's foreign key is usually a shadow property the write model created, so its name is read from
    // there rather than guessed. The guess is the fallback for a model that has not been built to mirror.
    private static string[] ChildForeignKey(IModel? writeModel, Type rootWriteType, ReadChildMapping child)
    {
        var childType = writeModel?.FindEntityType(child.ChildWriteType);

        var foreignKey = childType?
            .GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == rootWriteType);

        return foreignKey is null
            ? [rootWriteType.Name + nameof(Entity<int>.Id)]
            : [.. foreignKey.Properties.Select(static p => p.Name)];
    }

    // "Total.Amount" walks the complex properties a value object was mapped to; "Reference" is one step.
    private static IProperty? FindProperty(IEntityType entity, string path)
    {
        var segments = path.Split('.');
        ITypeBase current = entity;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current.FindComplexProperty(segments[i]) is not { } complex)
            {
                return null;
            }

            current = complex.ComplexType;
        }

        return current.FindProperty(segments[^1]);
    }
}
