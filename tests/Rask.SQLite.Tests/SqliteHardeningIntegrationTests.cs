using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// The hardening pragmas and PRAGMA optimize, verified against a live connection rather than by
// inspecting the generated script — a pragma SQLite silently ignores would still appear in the script.
public sealed class SqliteHardeningIntegrationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-sqlite-harden-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task The_hardening_pragmas_take_effect_on_a_live_connection()
    {
        await using var connection = await OpenThroughFactoryAsync();

        Assert.Equal("0", ReadPragma(connection, "trusted_schema"));
        Assert.Equal("1", ReadPragma(connection, "cell_size_check"));
        Assert.Equal("400", ReadPragma(connection, "analysis_limit"));
    }

    // PRAGMA optimize writes sqlite_stat1, which is what the query planner reads to choose between
    // indexes. Its presence after a run is the observable proof the pragma did something.
    [Fact]
    public async Task Optimize_refreshes_the_query_planner_statistics()
    {
        await using var connection = await OpenThroughFactoryAsync();

        await ExecuteAsync(connection, "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        await ExecuteAsync(connection, "CREATE INDEX ix_t_name ON t(name)");
        for (var i = 0; i < 200; i++)
        {
            await ExecuteAsync(connection, $"INSERT INTO t (name) VALUES ('n{i.ToString(CultureInfo.InvariantCulture)}')");
        }

        // Reading the index is what marks it as worth analysing.
        await ExecuteAsync(connection, "SELECT COUNT(*) FROM t WHERE name = 'n1'");

        Assert.Null(Scalar(connection, "SELECT name FROM sqlite_master WHERE name = 'sqlite_stat1'"));

        SqlitePragmas.Optimize(connection);

        Assert.Equal("sqlite_stat1", Scalar(connection, "SELECT name FROM sqlite_master WHERE name = 'sqlite_stat1'"));
    }

    // It runs on connections being torn down, so it must never be the thing that throws.
    [Fact]
    public void Optimize_on_a_closed_connection_is_a_no_op()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        SqlitePragmas.Optimize(connection);
    }

    private async Task<SqliteConnection> OpenThroughFactoryAsync()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite($"Data Source={_dbPath}");
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRaskSqliteConnectionFactory>();
        return await factory.CreateOpenAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static string ReadPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
