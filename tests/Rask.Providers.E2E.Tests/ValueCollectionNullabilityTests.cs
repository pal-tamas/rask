using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Rask.Data.Tests.Shapes;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// A collection of values is NULL in the database exactly where the model says it may be null, on the two server
/// databases: the columns, the DDL, an adopted NOT NULL table, and the migration from the owned mapping — which fills
/// the rows that were NULL with an empty array by itself.
/// </summary>
/// <remarks>
/// The model, the DDL and the migration's SQL are proved without a server — they need a provider but no connection.
/// The migration is also run against each server, which reports SKIPPED where there is none.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ValueCollectionNullabilityTests
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sql_server_a_column_is_null_only_where_the_model_says_the_collection_may_be()
    {
        using var context = OnSqlServer(OfflineSqlServer);

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
        using var context = new FilingContext(OnPostgres<FilingContext>(OfflinePostgres));

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
        using var legacy = new LegacyFilingContext(new DbContextOptionsBuilder<LegacyFilingContext>().UseSqlServer(OfflineSqlServer).Options);
        using var rask = OnSqlServer(OfflineSqlServer);

        var (forward, back) = ValueObjectMappingDiff.Between(legacy, rask);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void On_postgresql_an_adopted_not_null_json_column_is_the_column_rask_maps()
    {
        using var legacy = new LegacyFilingContext(new DbContextOptionsBuilder<LegacyFilingContext>().UseNpgsql(OfflinePostgres).Options);
        using var rask = new FilingContext(OnPostgres<FilingContext>(OfflinePostgres));

        var (forward, back) = ValueObjectMappingDiff.Between(legacy, rask);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void On_sql_server_the_migration_rask_generates_fills_the_null_rows_with_an_empty_array_first()
    {
        using var owned = new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseSqlServer(OfflineSqlServer).Options);
        using var rask = OnSqlServer(OfflineSqlServer);

        var sql = MigrationSql(owned, rask);

        Assert.Contains("UPDATE [ReturnFilings] SET [Files] = N'[]' WHERE [Files] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE [ReturnFilings] ALTER COLUMN [Files] nvarchar(max) NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("'{}'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void On_postgresql_the_migration_rask_generates_fills_the_null_rows_with_an_empty_array_first()
    {
        using var owned = new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseNpgsql(OfflinePostgres).Options);
        using var rask = new FilingContext(OnPostgres<FilingContext>(OfflinePostgres));

        var sql = MigrationSql(owned, rask);

        Assert.Contains("UPDATE \"ReturnFilings\" SET \"Files\" = '[]' WHERE \"Files\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN \"Files\" SET NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("'{}'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_a_sql_server_the_migrated_rows_that_were_null_read_back_as_empty_lists()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var connection = SqlServer.Database("rask_value_collections");
        await using (var owned = new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseSqlServer(connection).Options))
        {
            await owned.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
            await owned.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await owned.Database.ExecuteSqlRawAsync(
                "INSERT INTO [ReturnFilings] ([Files], [Labels]) VALUES (NULL, N'[]'), (N'[{\"Name\":\"a.pdf\",\"Pages\":1}]', N'[]')",
                TestContext.Current.CancellationToken);
        }

        await using var rask = OnSqlServer(connection);
        await MigrateAsync(new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseSqlServer(connection).Options), rask);
        var files = await FileCountsAsync(rask);
        await rask.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);

        Assert.Equal([0, 1], files);
    }

    [Fact]
    public async Task On_a_postgresql_server_the_migrated_rows_that_were_null_read_back_as_empty_lists()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        const string Schema = "value_collection_nullability";
        var connection = new NpgsqlConnectionStringBuilder(Postgres.Required) { SearchPath = Schema }.ConnectionString;
        await using (var owned = new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseNpgsql(connection).Options))
        {
            await Postgres.ResetSchemaAsync(owned, Schema);
            await owned.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"ReturnFilings\" (\"Files\", \"Labels\") VALUES (NULL, '{}'), ('[{\"Name\":\"a.pdf\",\"Pages\":1}]', '{}')",
                TestContext.Current.CancellationToken);
        }

        await using var rask = new FilingContext(OnPostgres<FilingContext>(connection));
        await MigrateAsync(new OwnedFilingContext(new DbContextOptionsBuilder<OwnedFilingContext>().UseNpgsql(connection).Options), rask);
        var files = await FileCountsAsync(rask);
        await Postgres.DropSchemaAsync(rask, Schema);

        Assert.Equal([0, 1], files);
    }

    private static async Task<List<int>> FileCountsAsync(FilingContext context) =>
        [.. (await context.Set<ReturnFiling>().OrderBy(f => f.Id).ToListAsync(TestContext.Current.CancellationToken)).Select(f => f.Files.Count)];

    // The path `dotnet ef migrations add` and `database update` take: the differ between the two models, then the generator.
    private static async Task MigrateAsync(DbContext from, DbContext to)
    {
        await using (from)
        {
            var model = to.GetService<IDesignTimeModel>().Model;
            var commands = to.GetService<IMigrationsSqlGenerator>().Generate(Differences(from, to), model);
            await to.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(
                commands, to.GetService<IRelationalConnection>(), TestContext.Current.CancellationToken);
        }
    }

    private static string MigrationSql(DbContext from, DbContext to) =>
        string.Join("\n", to.GetService<IMigrationsSqlGenerator>()
            .Generate(Differences(from, to), to.GetService<IDesignTimeModel>().Model)
            .Select(c => c.CommandText));

    private static IReadOnlyList<MigrationOperation> Differences(DbContext from, DbContext to) =>
        to.GetService<IMigrationsModelDiffer>().GetDifferences(
            from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            to.GetService<IDesignTimeModel>().Model.GetRelationalModel());

    // UseRaskPostgres, as an app configures its context.
    private static DbContextOptions<TContext> OnPostgres<TContext>(string connection)
        where TContext : DbContext => new DbContextOptionsBuilder<TContext>().UseRaskPostgresAt(connection).Options;

    // UseRaskSqlServer, as an app configures its context.
    private static FilingContext OnSqlServer(string connection) =>
        new(new DbContextOptionsBuilder<FilingContext>().UseRaskSqlServerAt(connection).Options);
}
