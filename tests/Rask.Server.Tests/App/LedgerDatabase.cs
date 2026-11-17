using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Server.Tests.App;

/// <summary>A throwaway SQLite file for <see cref="LedgerContext"/>'s migrations, read and written without EF.</summary>
internal sealed class LedgerDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-owns-schema-{Guid.NewGuid():N}.db");

    /// <summary>Unpooled, so a test's own reads never hold the file a host is about to migrate.</summary>
    public string Connection => $"Data Source={_path};Pooling=False";

    /// <summary>Whether anything has opened the database yet.</summary>
    public bool Exists => File.Exists(_path);

    public async Task Run(string sql)
    {
        await using var connection = new SqliteConnection(Connection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqliteConnection(Connection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The migrations the database's history shows applied, in order.</summary>
    public async Task<List<string>> History()
    {
        var options = new DbContextOptionsBuilder<LedgerContext>().UseSqlite(Connection).Options;
        await using var db = new LedgerContext(options);
        return [.. await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)];
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }
}
