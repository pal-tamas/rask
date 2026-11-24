using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// <c>UseRaskSqlServer</c> hands EF Core the compatibility level a real server reports: the database's own once it
/// exists, and the one the server's version gives a new database before it does.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerCompatibilityLevelTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_compatibility";

    // Created below its server's own level, so a level read off the server cannot be mistaken for a version mapping.
    private const int DatabaseLevel = 130;

    private const string MissingDatabase = "rask_e2e_compatibility_missing";

    // Made and dropped on master, not through a context: a context opened before the database exists would read — and
    // keep — the level of the server's version for this connection string, which is the other test's case.
    public async ValueTask InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await OnMasterAsync($"DROP DATABASE IF EXISTS [{DatabaseName}]; CREATE DATABASE [{DatabaseName}]; "
                + $"ALTER DATABASE [{DatabaseName}] SET COMPATIBILITY_LEVEL = {DatabaseLevel};");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await OnMasterAsync($"DROP DATABASE IF EXISTS [{DatabaseName}];");
        }
    }

    [Fact]
    public void An_existing_database_is_read_at_its_own_compatibility_level()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var options = Options(DatabaseName);

        Assert.Equal(DatabaseLevel, LevelOf(options));
    }

    [Fact]
    public async Task A_database_not_created_yet_takes_the_level_of_its_server_s_version()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var majorVersion = await ServerMajorVersionAsync();

        var options = Options(MissingDatabase);

        Assert.Equal(majorVersion * 10, LevelOf(options));
    }

    private static DbContextOptions Options(string database) =>
        new DbContextOptionsBuilder().UseRaskSqlServerAt(SqlServer.Database(database)).Options;

    private static async Task<int> ServerMajorVersionAsync() =>
        (int)(await OnMasterAsync("SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int);"))!;

    private static async Task<object?> OnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(SqlServer.Database("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

#pragma warning disable EF1001 // EF Core keeps the level on its internal options extension; there is no public reader.
    private static int LevelOf(DbContextOptions options) =>
        options.FindExtension<SqlServerOptionsExtension>()!.SqlServerCompatibilityLevel;
#pragma warning restore EF1001
}
