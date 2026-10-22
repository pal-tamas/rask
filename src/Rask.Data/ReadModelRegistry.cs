using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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

        // And a child partitioned through its parent asks the parent's FACE, so its filter needs the relationships.
        FilterThroughParents(modelBuilder, all, context);

        return modelBuilder;
    }

    private static void FilterThroughParents(ModelBuilder modelBuilder, List<ReadEntityMapping> all, DbContext? context)
    {
        var partitioned = all
            .Where(static m => ConventionRegistry.IsPerTenant(m.WriteType))
            .Select(static m => m.ReadType)
            .ToHashSet();

        foreach (var mapping in all)
        {
            var face = modelBuilder.Entity(mapping.ReadType).Metadata;
            if (!TenantParents.IsThroughParent(face) || context is null)
            {
                continue;
            }

            var keys = TenantParents.HeldKeysOf(face, partitioned.Contains);
            if (keys.Count == 0)
            {
                // Never left unfiltered: a child face with no column and no parent to ask would read every tenant.
                throw new InvalidOperationException(
                    $"'{mapping.WriteType.Name}' belongs to its tenant through its parent, but its read face has no " +
                    "relationship to a tenant-scoped parent to ask. Declare TenantId on it, or hold it in the " +
                    "collection of a tenant-scoped aggregate.");
            }

            modelBuilder.Entity(mapping.ReadType).HasQueryFilter(
                ModelBuilderExtensions.TenantFilter, TenantParents.BuildFilter(mapping.ReadType, keys, context));
        }
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
        builder.HasKey(KeyOf(mapping, write));

        foreach (var column in mapping.Columns)
        {
            MapColumn(builder, column, write, store);
        }

        foreach (var collection in mapping.ValueCollections)
        {
            MapValueCollection(builder, collection, write, store);
        }

        MapAnnotationsAndFilters(builder, mapping, write, context);
    }

    // One column each, so they are mapped the same way the write side maps them rather than as a property per element.
    // The kind is settled at compile time and carried here — whether an element is a value object is the generator's
    // rule to apply — unless the write side stores the column as plain values, which the face then reads element by
    // element through whatever converter the write side put on the element: an enum by its name, a value sealed the
    // app's way.
    private static void MapValueCollection(
        EntityTypeBuilder builder, ReadValueCollectionMapping collection, IEntityType? write, StoreObjectIdentifier? store)
    {
        var source = write?.FindProperty(collection.Member);

        if (collection.ValueObject is { } element && source is null)
        {
            var json = write?.FindNavigation(collection.Member)?.TargetEntityType.GetContainerColumnName();
            builder.OwnsMany(element, collection.Member, owned => owned.ToJson(json));
            return;
        }

        var primitive = builder.PrimitiveCollection(collection.Member);
        if (source is null)
        {
            return;
        }

        if ((store is { } table ? source.GetColumnName(table) : source.GetColumnName()) is { Length: > 0 } name)
        {
            primitive.HasColumnName(name);
        }

        if (source.GetColumnType() is { Length: > 0 } columnType)
        {
            primitive.HasColumnType(columnType);
        }

        if (source.GetElementType() is { } written)
        {
            MirrorElement(primitive.ElementType(), written);
        }
    }

    // The element's converter — written out, or only the type it is stored as (HasConversion<string>()), which EF Core
    // builds the converter from — and its facets, so the face reads each element the way the write side wrote it.
    private static void MirrorElement(ElementTypeBuilder element, IElementType written)
    {
        var converter = written.GetValueConverter() ??
                        (written.GetProviderClrType() is null ? null : written.FindTypeMapping()?.Converter);
        if (converter is not null)
        {
            element.HasConversion(converter, written.GetValueComparer());
        }

        if (written.GetMaxLength() is { } maxLength)
        {
            element.HasMaxLength(maxLength);
        }

        if (written.IsUnicode() is { } unicode)
        {
            element.IsUnicode(unicode);
        }
    }

    // The write model's key, member for member — Id, or the parts of a key of several columns. With no write model to
    // read, the generator's order answers: a key of several columns is the face's first columns, in the tuple's order.
    private static string[] KeyOf(ReadEntityMapping mapping, IEntityType? write)
    {
        if (write?.FindPrimaryKey() is { } key)
        {
            return [.. key.Properties.Select(static p => p.Name)];
        }

        return ModelBuilderExtensions.IdTypeOf(mapping.WriteType) is { } idType && ModelBuilderExtensions.IsComposite(idType)
            ? [.. mapping.Columns.Take(idType.GetGenericArguments().Length).Select(static c => c.Member)]
            : [nameof(Entity<int>.Id)];
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

        MirrorConversion(property, source);

        if (source.GetMaxLength() is { } maxLength)
        {
            property.HasMaxLength(maxLength);
        }

        if (source.IsUnicode() is { } unicode)
        {
            property.IsUnicode(unicode);
        }
    }

    // Every conversion the write side reads a column through, the face reads it through too — one that keeps the type
    // (bytes compressed to bytes, a string sealed to a string) included, or the face hands back what is stored. A face
    // holding the value inside a one-value value object reads Rask's own column as it is, and an app's converter
    // through the value object it was written for.
    private static void MirrorConversion(PropertyBuilder property, IProperty source)
    {
        // A converter written out, or only the type it is stored as (HasConversion<string>()), which EF Core builds
        // the converter from: the write model's type mapping holds the one in force.
        var converter = source.GetValueConverter() ??
                        (source.GetProviderClrType() is null ? null : source.FindTypeMapping()?.Converter);
        if (converter is null)
        {
            return;
        }

        var face = property.Metadata.ClrType;
        if ((Nullable.GetUnderlyingType(face) ?? face) == (Nullable.GetUnderlyingType(converter.ModelClrType) ?? converter.ModelClrType))
        {
            var comparer = source.GetValueComparer();
            property.HasConversion(converter, comparer.Type == face ? comparer : null);
            return;
        }

        if (OneValueConverters.ForValue(converter) is { } throughValueObject)
        {
            property.HasConversion(throughValueObject);
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
        if (ConventionRegistry.IsPerTenant(mapping.WriteType))
        {
            if (context is not ITenantScoped)
            {
                throw new InvalidOperationException(
                    $"'{mapping.WriteType.Name}' is tenant-scoped, but the read context " +
                    $"('{context?.GetType().Name ?? "none"}') cannot supply the current tenant. It must be " +
                    "declared ': DbContext, ITenantScoped'.");
            }

            // A child with no column of its own asks its parent's face, once the relationships are mapped.
            if (IsThroughParent(mapping, write))
            {
                builder.HasAnnotation(TenantParents.Annotation, true);
                return;
            }

            // In the write side's type, which is the column's: a face that compared an int column with a Guid
            // would match nothing on one provider and fail on another.
            var column = write is null ? TenantColumn.TypeFor(mapping.WriteType) : TenantColumn.TypeFor(write);

            var tenant = builder.Property(column, Columns.TenantId);
            MirrorDeclaredTenantColumn(
                tenant,
                write?.FindProperty(Columns.TenantId),
                write is null ? null : StoreObjectIdentifier.Create(write, StoreObjectType.Table));
            builder.HasQueryFilter(
                ModelBuilderExtensions.TenantFilter,
                TenantColumn.BuildFilter(mapping.ReadType, column, context));
        }
    }

    // The write model's answer; with none to mirror, a child that declares no TenantId — the write side's own rule.
    private static bool IsThroughParent(ReadEntityMapping mapping, IEntityType? write) =>
        write is not null
            ? TenantParents.IsThroughParent(write)
            : !mapping.IsRoot && mapping.WriteType.GetProperty(Columns.TenantId) is null;

    // A tenant column the write side declares and configured — kept as TEXT through a conversion, named as an existing
    // table names it — is read the way it is written. Left as the face's own shadow column it would compare a Guid
    // with text: nothing on one provider (the text's case differs) and a scan that fails on a stray value on another.
    private static void MirrorDeclaredTenantColumn(
        PropertyBuilder tenant, IProperty? declared, StoreObjectIdentifier? store)
    {
        if (declared is null)
        {
            return;
        }

        if ((store is { } table ? declared.GetColumnName(table) : declared.GetColumnName()) is { Length: > 0 } name)
        {
            tenant.HasColumnName(name);
        }

        if (declared.GetColumnType() is { Length: > 0 } columnType)
        {
            tenant.HasColumnType(columnType);
        }

        // Either a converter the author wrote, or only the type it is stored as (HasConversion<string>()), which EF
        // Core builds the converter from: the write model's type mapping holds the one in force.
        if ((declared.GetValueConverter() ?? declared.FindTypeMapping()?.Converter) is { } converter)
        {
            tenant.HasConversion(converter);
        }

        if (declared.GetMaxLength() is { } maxLength)
        {
            tenant.HasMaxLength(maxLength);
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
            // The target's collection of these rows, when the generator gave it one, is this same relationship
            // read from the other end — so it carries the referencing rows' own filters and nothing else.
            builder
                .HasOne(reference.TargetReadType, reference.Navigation)
                .WithMany(reference.Inverse)
                .HasForeignKey(reference.ForeignKey)
                .OnDelete(DeleteBehavior.NoAction);
        }

        foreach (var child in mapping.Children)
        {
            var foreignKey = ChildForeignKey(writeModel, mapping.WriteType, child);

            var relationship = modelBuilder
                .Entity(child.ChildReadType)
                .HasOne(mapping.ReadType, child.Inverse)
                .WithMany(child.Collection)
                .HasForeignKey(foreignKey is null
                    ? GuessedForeignKey(mapping.WriteType)
                    : [.. foreignKey.Properties.Select(static p => p.Name)])
                .OnDelete(DeleteBehavior.NoAction)
                .HasAnnotation(TenantParents.HeldAnnotation, true);

            // Optional where the write side's is: a child of two parents holds a NULL in one of its keys.
            if (foreignKey is { IsRequired: false })
            {
                relationship.IsRequired(false);
            }
        }
    }

    // The child's foreign key is usually a shadow property the write model created, so its name is read from
    // there rather than guessed. The guess is the fallback for a model that has not been built to mirror.
    private static IForeignKey? ChildForeignKey(IModel? writeModel, Type rootWriteType, ReadChildMapping child) =>
        writeModel?.FindEntityType(child.ChildWriteType)?
            .GetForeignKeys()
            .FirstOrDefault(fk => fk.PrincipalEntityType.ClrType == rootWriteType);

    // EF Core's own name for a foreign key to the parent: OrderId, or one column per part of a parent keyed by several
    // — ReportColumnReportId, ReportColumnColumnId.
    private static string[] GuessedForeignKey([DynamicallyAccessedMembers(DataTrimming.Entity)] Type parent) =>
        ModelBuilderExtensions.HasCompositeKey(parent) && ModelBuilderExtensions.KeyPartsOf(parent) is { } parts
            ? [.. parts.Select(part => parent.Name + part)]
            : [parent.Name + nameof(Entity<int>.Id)];

    // "Total.Amount" walks the complex properties a value object was mapped to; "Reference" is one step.
    private static IProperty? FindProperty(IEntityType entity, string path)
    {
        var segments = path.Split('.');
        ITypeBase current = entity;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current.FindComplexProperty(segments[i]) is not { } complex)
            {
                // "Email.Value" over a one-value value object: the write side stores it as the scalar column
                // `Email` itself, converted, so the value's column IS that property.
                return i == segments.Length - 2 && current.FindProperty(segments[i]) is { } scalar &&
                       scalar.GetValueConverter() is not null
                    ? scalar
                    : null;
            }

            current = complex.ComplexType;
        }

        return current.FindProperty(segments[^1]);
    }
}
