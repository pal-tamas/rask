using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>Names every key, index, foreign key and default the model did not name itself.</summary>
/// <remarks>
///     As the model is finalized, so it sees the whole of it — every battery's table and every entity's own
///     <c>Configure</c> — and at convention strength, so a name stated explicitly (<c>HasDatabaseName</c>,
///     <c>HasConstraintName</c>, <c>[Index(Name = …)]</c>) is left as it was. A name longer than the provider allows is
///     cut to its limit: 128 on SQL Server, 63 on PostgreSQL.
/// </remarks>
internal sealed class ConstraintNameConvention(Func<Constraint, string?> name, bool namesDefaults) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var limit = modelBuilder.Metadata.GetMaxIdentifierLength();

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.GetTableName() is not { } table)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (var key in entityType.GetDeclaredKeys())
            {
                var kind = key.IsPrimaryKey() ? ConstraintKind.PrimaryKey : ConstraintKind.Unique;
                Name(kind, table, store, key.Properties, limit, key.Builder.HasName);
            }

            foreach (var index in entityType.GetDeclaredIndexes())
            {
                var kind = index.IsUnique ? ConstraintKind.Unique : ConstraintKind.Index;
                Name(kind, table, store, index.Properties, limit, index.Builder.HasDatabaseName);
            }

            foreach (var foreignKey in entityType.GetDeclaredForeignKeys().Where(k => IsConstraint(k, table)))
            {
                Name(ConstraintKind.ForeignKey, table, store, foreignKey.Properties, limit, foreignKey.Builder.HasConstraintName);
            }

            if (namesDefaults && !string.Equals(entityType.GetMappingStrategy(), RelationalAnnotationNames.TpcMappingStrategy, StringComparison.Ordinal))
            {
                foreach (var property in entityType.GetDeclaredProperties().Where(NamesItsDefaultByConvention))
                {
                    Name(ConstraintKind.Default, table, store, [property], limit, property.SetDefaultConstraintName);
                }
            }
        }
    }

    // A foreign key between two halves of one table (an owned type, a table split) is not a constraint in the database.
    private static bool IsConstraint(IConventionForeignKey key, string table) =>
        key.PrincipalEntityType.GetTableName() is { } principal &&
        !(key.IsOwnership && string.Equals(principal, table, StringComparison.Ordinal));

    // A default with no name stated: the metadata setter would overwrite one, so the source is asked first (the builders
    // used for every other kind ask it themselves).
    private static bool NamesItsDefaultByConvention(IConventionProperty property) =>
        (property.GetDefaultValueSql() is not null || property.TryGetDefaultValue(out _)) &&
        property.GetDefaultConstraintNameConfigurationSource() is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation);

    private void Name(
        ConstraintKind kind,
        string table,
        StoreObjectIdentifier store,
        IReadOnlyList<IConventionProperty> properties,
        int? limit,
        Func<string?, bool, object?> set)
    {
        var columns = properties.Select(p => p.GetColumnName(store) ?? p.GetColumnName()).ToArray();
        if (name(new Constraint(kind, table, columns)) is not { Length: > 0 } named)
        {
            return;
        }

        set(limit is { } max && named.Length > max ? named[..max] : named, false);
    }
}
