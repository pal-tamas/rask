// EF1001: NpgsqlMigrationsSqlGenerator's constructor takes Npgsql's singleton options from an Internal namespace. Deriving
// from the provider's generator is the only seam that adds DDL to a migration while keeping everything Npgsql emits.
#pragma warning disable EF1001

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
/// Npgsql's migrations, plus the one object PostgreSQL full-text search needs that no model annotation describes: the
/// <c>rask_unicode</c> text search configuration (#1109).
/// </summary>
/// <remarks>
/// The column and its GIN index are Npgsql's own (see <see cref="PostgresFullTextSearchConvention" />). What is added
/// here runs first in any migration that creates or changes a table searching with <see cref="FullTextTokenizer.Unicode" />,
/// and is idempotent, so a later migration running it again changes nothing.
/// </remarks>
internal sealed class RaskPostgresMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlOptions)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlOptions), IFullTextSearchEnforcer
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var commands = base.Generate(operations, model, options);
        if (model is null || !NeedsUnicodeConfiguration(operations, model))
        {
            return commands;
        }

        var builder = new MigrationCommandListBuilder(Dependencies);
        builder.AppendLines(PostgresFullTextSearch.CreateUnicodeConfiguration);
        builder.EndCommand();
        return [.. builder.GetCommandList(), .. commands];
    }

    private static bool NeedsUnicodeConfiguration(IReadOnlyList<MigrationOperation> operations, IModel model)
    {
        var tables = model.GetEntityTypes()
            .Where(e => FullTextSearchSpec.TryParse(e.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec)
                        && spec.Tokenizer == FullTextTokenizer.Unicode)
            .Select(e => e.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return tables.Count > 0 && operations.Any(o => o switch
        {
            CreateTableOperation create => tables.Contains(create.Name),
            AddColumnOperation add => tables.Contains(add.Table),
            AlterColumnOperation alter => tables.Contains(alter.Table),
            _ => false,
        });
    }
}
