using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Root-causing a rare SQLITE_BUSY that escaped the retry under mixed read/write load: does opening a
// connection while another connection holds the write lock throw, and does it depend on configureRetry?
public sealed class RaskSqliteOpenUnderLockTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-open-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    [Theory]
    [InlineData(true)]  // configureRetry supplied -> pragma busy_timeout=0
    [InlineData(false)] // no retry              -> pragma busy_timeout=5000
    public async Task Opening_a_connection_while_the_write_lock_is_held(bool retryEnabled)
    {
        await CreateDatabaseAsync();

        var builder = new DbContextOptionsBuilder<ProbeDbContext>();
        var options = retryEnabled
            ? builder.UseRaskSqlite(ConnectionString, o => { o.Retry.Enabled = true; o.Retry.Timeout = TimeSpan.FromSeconds(30); }).Options
            : builder.UseRaskSqlite(ConnectionString).Options;

        // Hold the write lock from an unrelated connection, as a concurrent writer would.
        await using var holder = new SqliteConnection(ConnectionString);
        await holder.OpenAsync();
        using var holderTx = holder.BeginImmediate();

        // A plain read on a fresh context. In WAL a reader never blocks on a writer, so this must succeed --
        // unless something in the open path itself needs the lock.
        await using var context = new ProbeDbContext(options);
        var exception = await Record.ExceptionAsync(() => context.Rows.CountAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task The_pragma_script_is_what_takes_the_lock()
    {
        await CreateDatabaseAsync();

        await using var holder = new SqliteConnection(ConnectionString);
        await holder.OpenAsync();
        using var holderTx = holder.BeginImmediate();

        // Exactly what the interceptor runs on every open when configureRetry is supplied: busy_timeout=0
        // followed by the lock-taking journal_mode.
        var script = SqlitePragmas.BuildScript(new SqliteOptions { BusyTimeout = TimeSpan.Zero });

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = script;

        var exception = Record.Exception(() => command.ExecuteNonQuery());
        Assert.Null(exception);
    }

    private async Task CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>().UseRaskSqlite(ConnectionString).Options;
        await using var context = new ProbeDbContext(options);
        await context.Database.EnsureCreatedAsync();
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
