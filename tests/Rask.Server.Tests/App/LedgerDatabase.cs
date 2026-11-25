using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Tests.Shared;

namespace Rask.Server.Tests.App;

/// <summary>A throwaway SQLite database for <see cref="LedgerContext"/>'s migrations, read and written without EF.</summary>
internal sealed class LedgerDatabase : IDisposable
{
    private readonly InMemorySqlite _database = new();

    public string Connection => _database.ConnectionString;

    /// <summary>Whether the database still has no schema at all — nothing has created a table in it.</summary>
    public async Task<bool> IsEmpty() => await Scalar("SELECT COUNT(*) FROM sqlite_master") is 0L;

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

    public void Dispose() => _database.Dispose();
}
