using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Integration tests for the EF Core busy-retry: UseRaskSqlite(configureRetry) turns SQLite's native busy
// handler off and registers the fair-interval RaskSqliteExecutionStrategy so SaveChanges rides out a
// contended write lock instead of throwing "database is locked".
public sealed class RaskSqliteExecutionStrategyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-ef-retry-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    [Fact]
    public async Task Enabling_retry_turns_off_the_native_busy_handler()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite(ConnectionString, configureRetry: _ => { })
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        // busy_timeout=0 hands all waiting to the async execution strategy.
        Assert.Equal("0", ReadPragma(connection, "busy_timeout"));

        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Without_retry_the_native_busy_timeout_stays_at_the_default()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite(ConnectionString)
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        Assert.Equal("5000", ReadPragma(connection, "busy_timeout"));

        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task SaveChanges_retries_and_succeeds_when_a_held_lock_is_released()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite(ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromSeconds(10))
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // A separate connection takes the write lock, then releases it after ~100 ms.
        await using var holder = new SqliteConnection(ConnectionString);
        await holder.OpenAsync();
        var holderTx = holder.BeginImmediate();
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            holderTx.Commit();
        });

        context.Rows.Add(new ProbeRow());
        await context.SaveChangesAsync(); // must wait out the lock, not throw

        await release;
        Assert.Equal(1, await context.Rows.CountAsync());
    }

    [Fact]
    public async Task SaveChanges_throws_busy_when_the_lock_is_never_released()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite(ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromMilliseconds(200))
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.EnsureCreatedAsync();

        await using var holder = new SqliteConnection(ConnectionString);
        await holder.OpenAsync();
        using var holderTx = holder.BeginImmediate();

        context.Rows.Add(new ProbeRow());
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());

        Assert.True(HasBusy(exception), $"expected SQLITE_BUSY in the exception chain, got {exception}");
    }

    private static bool HasBusy(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode == 5)
            {
                return true;
            }
        }

        return false;
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
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }
    }
}
