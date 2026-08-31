using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// Exercises the on-device data mechanism a SQLite-backed store relies on: CRUD through the
// raw ISqlite against a real file database, and — the point of persistence — that
// rows written on one connection are still there on a fresh one (as they would be after an app restart).
public sealed class RaskSqlitePersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-persist-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Rows_written_on_one_connection_are_visible_on_a_fresh_one()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite($"Data Source={_dbPath}");
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISqlite>();

        // Write via one connection (create the table + insert), then dispose it.
        await using (var write = await factory.CreateOpenAsync())
        {
            Execute(write, "CREATE TABLE todos(id TEXT PRIMARY KEY, title TEXT NOT NULL, completed INTEGER NOT NULL);");
            Execute(write, "INSERT INTO todos VALUES('a', 'Buy milk', 0);");
            Execute(write, "INSERT INTO todos VALUES('b', 'Ship it', 1);");
        }

        // A brand-new connection from the factory (WAL is on) sees the committed rows — durability across
        // the connection lifetime, which on-device is what survives an app restart.
        await using var read = await factory.CreateOpenAsync();
        using var command = read.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM todos;";
        Assert.Equal(2L, (long)command.ExecuteScalar()!);

        Assert.Equal("wal", ReadScalar(read, "PRAGMA journal_mode;"));
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ReadScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
