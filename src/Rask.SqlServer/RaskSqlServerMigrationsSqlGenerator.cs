using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Rask.Data;

namespace Rask.SqlServer;

/// <summary>
///     SQL Server's migrations, with the rewrite of a column of moments from the app's wall clock to UTC
///     (<see cref="MomentsInUtc" />) written out as the <c>UPDATE</c> it is, in the app's zone as the migration applies.
/// </summary>
/// <remarks>
///     <c>AT TIME ZONE</c> reads a wall time the clock showed twice as its first occurrence and moves one it skipped
///     forward — the rule a <c>Moment</c> follows — so the rows say what the app would have stored. The zone is SQL
///     Server's name for it (<c>Central Europe Standard Time</c> for <c>Europe/Budapest</c>), which SQL Server on Linux
///     reads too. In an app whose zone is UTC the wall clock was UTC already, and nothing is rewritten.
/// </remarks>
internal sealed class RaskSqlServerMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer)
    : SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
{
    public override IReadOnlyList<MigrationCommand> Generate(
        IReadOnlyList<MigrationOperation> operations,
        IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default) =>
        base.Generate(MomentsInUtc.Written(operations, Rewrite), model, options);

    private string? Rewrite(string table, string? schema, string column)
    {
        var zone = Clock.Zone;
        if (SqlServerZone.IsUtc(zone))
        {
            return null;
        }

        var name = SqlServerZone.Of(zone);
        var sql = Dependencies.SqlGenerationHelper;
        var quoted = sql.DelimitIdentifier(column);
        return $"UPDATE {sql.DelimitIdentifier(table, schema)} SET {quoted} = CAST(({quoted} AT TIME ZONE N'{name.Replace("'", "''", StringComparison.Ordinal)}') "
               + $"AT TIME ZONE 'UTC' AS datetime2) WHERE {quoted} IS NOT NULL{sql.StatementTerminator}";
    }
}
