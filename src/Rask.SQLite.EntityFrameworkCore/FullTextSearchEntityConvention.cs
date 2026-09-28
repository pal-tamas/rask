using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// Maps each declared full-text index — and, for a table without an <c>INTEGER</c> key, its key map — as a keyless
/// property-bag entity, so a query can join to it like to any table.
/// </summary>
/// <remarks>
/// <para>
/// Both are excluded from migrations: <see cref="FullTextSearchDdl"/> creates them, because EF Core has no way to
/// express a virtual table. They exist in the model only so the query pipeline can name their columns.
/// </para>
/// <para>
/// FTS5 exposes three columns a query needs that are not in the declaration: <c>rowid</c>; a hidden column named
/// after the table itself, where <c>index = 'words'</c> is a full-text match; and <c>rank</c>, the bm25 score (lower
/// is better). The row id is typed as the entity's own key where it IS the key, because comparing an <c>int</c> key
/// to a <c>long</c> makes EF Core emit a <c>CAST</c> that stops SQLite seeking the primary key.
/// </para>
/// </remarks>
internal sealed class FullTextSearchEntityConvention : IModelFinalizingConvention
{
    public const string RowId = "RowId";
    public const string Match = "Match";
    public const string Rank = "Rank";

    public static string IndexEntityName(IReadOnlyEntityType entityType) => $"{entityType.Name}.FullTextIndex";

    public static string KeyEntityName(IReadOnlyEntityType entityType) => $"{entityType.Name}.FullTextKeys";

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.FindAnnotation(FullTextSearchSpec.AnnotationName) is null
                || entityType.GetTableName() is not { } table
                || entityType.FindPrimaryKey() is not { } key)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            var usesRowid = FullTextSearchDdl.UsesRowid(entityType);

            // Recorded, so it travels into the migration's saved model and the DDL builds the layout queries expect.
            entityType.SetAnnotation(
                FullTextSearchDdl.LayoutAnnotation,
                usesRowid ? FullTextSearchDdl.RowidLayout : FullTextSearchDdl.KeyMapLayout);

            var index = Bag(modelBuilder, IndexEntityName(entityType), FullTextSearchDdl.IndexTable(table), entityType.GetSchema());
            if (usesRowid)
            {
                KeyColumn(index, key.Properties[0], RowId, "rowid", store);
            }
            else
            {
                Column(index, typeof(long), RowId, "rowid");
            }

            Column(index, typeof(string), Match, FullTextSearchDdl.IndexTable(table));
            Column(index, typeof(double), Rank, "rank");

            if (!usesRowid)
            {
                var keys = Bag(modelBuilder, KeyEntityName(entityType), FullTextSearchDdl.KeyTable(table), entityType.GetSchema());
                Column(keys, typeof(long), RowId, "rowid");

                foreach (var property in key.Properties)
                {
                    KeyColumn(keys, property, property.Name, property.GetColumnName(store) ?? property.Name, store);
                }
            }
        }
    }

    private static IConventionEntityTypeBuilder Bag(
        IConventionModelBuilder modelBuilder,
        string name,
        string table,
        string? schema)
    {
        var builder = modelBuilder.SharedTypeEntity(name, typeof(Dictionary<string, object>))
            ?? throw new InvalidOperationException($"The full-text index entity '{name}' could not be added to the model.");

        builder.HasNoKey();
        builder.ToTable(table, schema);
        builder.Metadata.SetIsTableExcludedFromMigrations(true);
        return builder;
    }

    // A column holding the searched entity's key: same CLR type, same conversion, same store type, so comparing the two
    // needs no CAST — and a strongly-typed id still reads back as itself.
    private static void KeyColumn(
        IConventionEntityTypeBuilder entity,
        IConventionProperty key,
        string name,
        string column,
        StoreObjectIdentifier store)
    {
        var property = Column(entity, key.ClrType, name, column);

        if (key.GetValueConverter() is { } converter)
        {
            property.HasConversion(converter);
        }
        else if (FullTextSearchDdl.ValueConverterTypeOf(key) is { } converterType)
        {
            property.HasConverter(converterType);
        }

        if (key.GetColumnType(store) is { } columnType)
        {
            property.HasColumnType(columnType);
        }
    }

    private static IConventionPropertyBuilder Column(IConventionEntityTypeBuilder entity, Type type, string name, string column)
    {
        var property = entity.IndexerProperty(type, name)
            ?? throw new InvalidOperationException($"The full-text column '{name}' could not be added to '{entity.Metadata.Name}'.");

        property.HasColumnName(column);
        return property;
    }
}
