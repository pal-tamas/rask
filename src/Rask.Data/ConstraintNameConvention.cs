using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Rask.Generators.Shared;

namespace Rask.Data;

/// <summary>Names every key, index, foreign key, check and default the model did not name itself.</summary>
/// <remarks>
///     As the model is finalized, so it sees the whole of it — every battery's table and every entity's own
///     <c>Configure</c> — and at convention strength, so a name stated explicitly (<c>HasDatabaseName</c>,
///     <c>HasConstraintName</c>, <c>[Index(Name = …)]</c>, <c>HasCheckConstraint</c>) is left as it was. The rule is
///     <paramref name="naming" />'s, read here, so an app's <c>ConstraintNames</c> replaces Rask's wherever it is called.
///     A name longer than the provider allows is cut to its limit (128 on SQL Server, 63 on PostgreSQL) and ends in a
///     hash of the whole name.
/// </remarks>
internal sealed class ConstraintNameConvention(StoreNameConvention naming, bool namesDefaults) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        if (naming.Constraints is not { } name)
        {
            return;
        }

        var limit = modelBuilder.Metadata.GetMaxIdentifierLength();

        // The tenant references TenantReferences<T>() declares are foreign keys too, though not the model's.
        TenantReferenceDeclaration.Declare(modelBuilder, name);

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
                Name(name, kind, table, store, key.Properties, key.GetName(), limit, key.Builder.HasName);
            }

            foreach (var index in entityType.GetDeclaredIndexes())
            {
                // An index naming a complex property (EF Core 11) keeps the name EF gives it: it has no columns to name it by.
                if (IndexProperties.Scalars<IConventionProperty>(index.Properties) is { } columns)
                {
                    var kind = index.IsUnique ? ConstraintKind.Unique : ConstraintKind.Index;
                    Name(name, kind, table, store, columns, index.GetDatabaseName(), limit, index.Builder.HasDatabaseName);
                }
            }

            foreach (var foreignKey in entityType.GetDeclaredForeignKeys().Where(k => IsConstraint(k, table)))
            {
                Name(name, ConstraintKind.ForeignKey, table, store, foreignKey.Properties, foreignKey.GetConstraintName(), limit, foreignKey.Builder.HasConstraintName);
            }

            foreach (var check in entityType.GetDeclaredCheckConstraints().Where(NamesItsCheckByConvention))
            {
                if (name(new Constraint(ConstraintKind.Check, table, [], check.ModelName)) is { Length: > 0 } named)
                {
                    check.Builder.HasName(Cut(named, limit));
                }
            }

            if (namesDefaults && !string.Equals(entityType.GetMappingStrategy(), RelationalAnnotationNames.TpcMappingStrategy, StringComparison.Ordinal))
            {
                foreach (var property in entityType.GetDeclaredProperties().Where(NamesItsDefaultByConvention))
                {
                    Name(name, ConstraintKind.Default, table, store, [property], property.GetDefaultConstraintName(), limit, property.SetDefaultConstraintName);
                }
            }
        }
    }

    // A foreign key between two halves of one table (an owned type, a table split) is not a constraint in the database.
    private static bool IsConstraint(IConventionForeignKey key, string table) =>
        key.PrincipalEntityType.GetTableName() is { } principal &&
        !(key.IsOwnership && string.Equals(principal, table, StringComparison.Ordinal));

    // A default with no name stated: the metadata setter would overwrite one, so the source is asked first (the builders
    // used for every other kind ask it themselves). Default(…) counts whichever of the two
    // conventions runs first: its SQL is written as the model is finalized too.
    private static bool NamesItsDefaultByConvention(IConventionProperty property) =>
        (property.GetDefaultValueSql() is not null || property.TryGetDefaultValue(out _)
         || property.FindAnnotation(DatabaseDefaultConvention.Annotation) is not null) &&
        property.GetDefaultConstraintNameConfigurationSource() is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation);

    private static bool NamesItsCheckByConvention(IConventionCheckConstraint check) =>
        check.GetNameConfigurationSource() is not (ConfigurationSource.Explicit or ConfigurationSource.DataAnnotation);

    private static string Cut(string named, int? limit) => limit is { } max ? StoreNames.Fit(named, max) : named;

    private static void Name(
        Func<Constraint, string?> name,
        ConstraintKind kind,
        string table,
        StoreObjectIdentifier store,
        IReadOnlyList<IConventionProperty> properties,
        string? declared,
        int? limit,
        Func<string?, bool, object?> set)
    {
        var columns = properties.Select(p => p.GetColumnName(store) ?? p.GetColumnName()).ToArray();
        if (name(new Constraint(kind, table, columns, declared)) is not { Length: > 0 } named)
        {
            return;
        }

        set(Cut(named, limit), false);
    }
}
