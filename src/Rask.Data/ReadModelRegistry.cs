using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>One column on a read face, and where it comes from on the write model.</summary>
/// <param name="member">The property on the read face — <c>TotalAmount</c>.</param>
/// <param name="path">
///     The dotted path to it on the write model — <c>Total.Amount</c> — used to find what EF actually decided.
/// </param>
/// <param name="columnName">
///     The column the convention says it lands on. The mirror overrides this wherever the write model
///     disagrees, so it only decides for a property the write model has nothing to say about.
/// </param>
public sealed class ReadColumnMapping(string member, string path, string columnName)
{
    /// <summary>The property on the read face.</summary>
    public string Member { get; } = member;

    /// <summary>The dotted path to the same value on the write model.</summary>
    public string Path { get; } = path;

    /// <summary>The column name the convention derives.</summary>
    public string ColumnName { get; } = columnName;
}

/// <summary>A navigation on a read face, inferred from an id the write model holds.</summary>
/// <param name="navigation">The navigation's name — <c>ShippedByUser</c>.</param>
/// <param name="targetReadType">The read face it points at — <c>UserRead</c>.</param>
/// <param name="foreignKey">The id property it is inferred from — <c>ShippedByUserId</c>.</param>
public sealed class ReadReferenceMapping(string navigation, Type targetReadType, string foreignKey)
{
    /// <summary>The navigation's name.</summary>
    public string Navigation { get; } = navigation;

    /// <summary>The read face it points at.</summary>
    public Type TargetReadType { get; } = targetReadType;

    /// <summary>The id property it is inferred from.</summary>
    public string ForeignKey { get; } = foreignKey;
}

/// <summary>A collection of children on a read face, and the navigation back from each child.</summary>
/// <param name="collection">The collection on the root's read face — <c>Lines</c>.</param>
/// <param name="childReadType">The child's read face — <c>OrderLineRead</c>.</param>
/// <param name="childWriteType">The child's write type, for finding the relationship EF built.</param>
/// <param name="inverse">The navigation back to the root on the child's read face — <c>Order</c>.</param>
public sealed class ReadChildMapping(string collection, Type childReadType, Type childWriteType, string inverse)
{
    /// <summary>The collection on the root's read face.</summary>
    public string Collection { get; } = collection;

    /// <summary>The child's read face.</summary>
    public Type ChildReadType { get; } = childReadType;

    /// <summary>The child's write type.</summary>
    public Type ChildWriteType { get; } = childWriteType;

    /// <summary>The navigation back to the root on the child's read face.</summary>
    public string Inverse { get; } = inverse;
}

/// <summary>Everything one read face is mapped from.</summary>
/// <param name="readType">The generated read face — <c>OrderRead</c>.</param>
/// <param name="writeType">The entity it reads — <c>Order</c>.</param>
/// <param name="isRoot">Whether the entity is an aggregate root, which is what has a version and a soft delete.</param>
/// <param name="tableName">The table the convention says it lives in, overridden by the mirror.</param>
/// <param name="columns">Its columns.</param>
/// <param name="references">Its inferred navigations.</param>
/// <param name="children">Its child collections.</param>
public sealed class ReadEntityMapping(
    Type readType,
    Type writeType,
    bool isRoot,
    string tableName,
    IReadOnlyList<ReadColumnMapping> columns,
    IReadOnlyList<ReadReferenceMapping> references,
    IReadOnlyList<ReadChildMapping> children)
{
    /// <summary>The generated read face.</summary>
    public Type ReadType { get; } = readType;

    /// <summary>The entity it reads.</summary>
    public Type WriteType { get; } = writeType;

    /// <summary>Whether the entity is an aggregate root.</summary>
    public bool IsRoot { get; } = isRoot;

    /// <summary>The table the convention derives.</summary>
    public string TableName { get; } = tableName;

    /// <summary>Its columns.</summary>
    public IReadOnlyList<ReadColumnMapping> Columns { get; } = columns;

    /// <summary>Its inferred navigations.</summary>
    public IReadOnlyList<ReadReferenceMapping> References { get; } = references;

    /// <summary>Its child collections.</summary>
    public IReadOnlyList<ReadChildMapping> Children { get; } = children;
}

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
    public static ModelBuilder Apply(ModelBuilder modelBuilder, IModel? writeModel)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var all = Contributions.Values.SelectMany(static c => c).ToList();

        foreach (var mapping in all)
        {
            MapColumns(modelBuilder, mapping, writeModel?.FindEntityType(mapping.WriteType));
        }

        // Relationships last: both ends have to be entity types before one can point at the other.
        foreach (var mapping in all)
        {
            MapRelationships(modelBuilder, mapping, writeModel);
        }

        return modelBuilder;
    }

    private static void MapColumns(ModelBuilder modelBuilder, ReadEntityMapping mapping, IEntityType? write)
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
            var source = write is null ? null : FindProperty(write, column.Path);

            // The write model knows this entity and does not have the property: it was ignored, renamed out
            // of existence, or is not stored at all. Mapping it anyway would ask for a column the table has
            // not got, and the first query would fail on something the author never wrote.
            if (write is not null && source is null)
            {
                builder.Ignore(column.Member);
                continue;
            }

            var property = builder.Property(column.Member);

            if (source is null)
            {
                property.HasColumnName(column.ColumnName);
                continue;
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

        if (mapping.IsRoot)
        {
            builder.HasQueryFilter(ModelBuilderExtensions.BuildNotDeletedFilter(builder, mapping.ReadType));
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
