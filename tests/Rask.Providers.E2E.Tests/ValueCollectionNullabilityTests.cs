using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data.Tests.Shapes;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// A collection of values is NULL in the database exactly where the model says it may be null, on the two server
/// databases: the columns, the DDL, an adopted NOT NULL table, and the migration from the owned mapping.
/// </summary>
/// <remarks>
/// Proved without a server — a model, its DDL and a migration's SQL need a provider but no connection.
/// </remarks>
public sealed class ValueCollectionNullabilityTests(ITestOutputHelper output)
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sql_server_a_column_is_null_only_where_the_model_says_the_collection_may_be()
    {
        using var context = new FilingContext(SqlServer<FilingContext>());

        var columns = ValueObjectMappingDiff.Columns(context, ReturnFiling.Table);
        var script = context.Database.GenerateCreateScript();

        Assert.Equal(
            [
                "Aliases nvarchar(max) NULL default=-",
                "Attachments nvarchar(max) NULL default=-",
                "Files nvarchar(max) NOT NULL default=-",
                "Id int NOT NULL default=-",
                "Labels nvarchar(max) NOT NULL default=-",
            ],
            columns);
        Assert.Contains("[Files] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[Attachments] nvarchar(max) NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void On_postgresql_a_column_is_null_only_where_the_model_says_the_collection_may_be()
    {
        using var context = new FilingContext(Postgres<FilingContext>());

        var columns = ValueObjectMappingDiff.Columns(context, ReturnFiling.Table);
        var script = context.Database.GenerateCreateScript();

        Assert.Equal(
            [
                "Aliases text[] NULL default=-",
                "Attachments jsonb NULL default=-",
                "Files jsonb NOT NULL default=-",
                "Id integer NOT NULL default=-",
                "Labels text[] NOT NULL default=-",
            ],
            columns);
        Assert.Contains("\"Files\" jsonb NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Attachments\" jsonb,", script, StringComparison.Ordinal);
    }

    [Fact]
    public void On_sql_server_an_adopted_not_null_json_column_is_the_column_rask_maps()
    {
        using var legacy = new LegacyFilingContext(SqlServer<LegacyFilingContext>());
        using var rask = new FilingContext(SqlServer<FilingContext>());

        var (forward, back) = ValueObjectMappingDiff.Between(legacy, rask);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void On_postgresql_an_adopted_not_null_json_column_is_the_column_rask_maps()
    {
        using var legacy = new LegacyFilingContext(Postgres<LegacyFilingContext>());
        using var rask = new FilingContext(Postgres<FilingContext>());

        var (forward, back) = ValueObjectMappingDiff.Between(legacy, rask);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void On_sql_server_the_migration_with_an_empty_array_as_its_default_fills_the_null_rows_first()
    {
        using var owned = new OwnedFilingContext(SqlServer<OwnedFilingContext>());
        using var rask = new FilingContext(SqlServer<FilingContext>());

        var sql = MigrationSql(owned, rask);

        Assert.Contains("UPDATE [ReturnFilings] SET [Files] = N'[]' WHERE [Files] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE [ReturnFilings] ALTER COLUMN [Files] nvarchar(max) NOT NULL", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void On_postgresql_the_migration_with_an_empty_array_as_its_default_fills_the_null_rows_first()
    {
        using var owned = new OwnedFilingContext(Postgres<OwnedFilingContext>());
        using var rask = new FilingContext(Postgres<FilingContext>());

        var sql = MigrationSql(owned, rask);

        Assert.Contains("UPDATE \"ReturnFilings\" SET \"Files\" = '[]' WHERE \"Files\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN \"Files\" SET NOT NULL", sql, StringComparison.Ordinal);
    }

    // The SQL of the migration `dotnet ef migrations add` writes, with the one edit docs/data.md describes.
    private string MigrationSql(DbContext from, DbContext to)
    {
        var operations = to.GetService<IMigrationsModelDiffer>().GetDifferences(
            from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            to.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        operations.OfType<AlterColumnOperation>().Single(a => a.Name == "Files").DefaultValue = "[]";

        var sql = string.Join("\n", to.GetService<IMigrationsSqlGenerator>()
            .Generate(operations, to.GetService<IDesignTimeModel>().Model)
            .Select(c => c.CommandText));
        output.WriteLine(sql);
        return sql;
    }

    private static DbContextOptions<TContext> SqlServer<TContext>()
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseSqlServer(OfflineSqlServer).Options;

    private static DbContextOptions<TContext> Postgres<TContext>()
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseNpgsql(OfflinePostgres).Options;
}
