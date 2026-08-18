using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SQLitePCL;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Pins the transaction mode EF Core actually gets from Microsoft.Data.Sqlite, because docs/sqlite.md
// makes a claim about it and a driver change would silently invalidate that claim.
//
// SqliteTransaction composes its statement as
//     IsolationLevel == IsolationLevel.Serializable && !deferred ? "BEGIN IMMEDIATE;" : "BEGIN;"
// and SqliteConnection.BeginTransaction(IsolationLevel) passes deferred: level == ReadUncommitted.
// EF Core's RelationalConnection asks for IsolationLevel.Unspecified, which normalises to Serializable
// — so every transaction EF opens on SQLite, implicit or explicit, is already BEGIN IMMEDIATE. Nothing
// in Rask has to ask for it. ReadUncommitted is the one deferred trapdoor, and it doubles as this
// file's negative control: without it, a probe that always reported "locked" would pass everything.
public sealed class RaskSqliteTransactionModeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-txn-mode-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Ef_explicit_transaction_takes_the_write_lock_up_front()
    {
        await using var context = await CreateSchemaAsync();

        Assert.False(WriteLockHeldByAnotherConnection(), "the database must start unlocked");

        await using (var transaction = context.Database.BeginTransaction())
        {
            Assert.True(WriteLockHeldByAnotherConnection());
            await transaction.RollbackAsync();
        }

        Assert.False(WriteLockHeldByAnotherConnection());
    }

    [Fact]
    public async Task Ef_explicit_async_transaction_takes_the_write_lock_up_front()
    {
        await using var context = await CreateSchemaAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();

        Assert.True(WriteLockHeldByAnotherConnection());

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Ef_implicit_SaveChanges_transaction_takes_the_write_lock_up_front()
    {
        await using (var schema = await CreateSchemaAsync())
        {
            // schema only
        }

        // The interceptor runs after EF has opened its implicit transaction but before the first
        // command executes — the only window where DEFERRED and IMMEDIATE look different. Once an
        // INSERT runs, the write lock is held either way.
        var probe = new WriteLockProbeInterceptor(WriteLockHeldByAnotherConnection);
        await using var context = new ProbeDbContext(OptionsWith(probe));

        // Two rows, so EF opens a transaction rather than sending a single auto-committed command.
        context.Rows.Add(new ProbeRow());
        context.Rows.Add(new ProbeRow());
        await context.SaveChangesAsync();

        Assert.True(probe.LockHeldBeforeFirstCommand.HasValue, "the interceptor never fired");
        Assert.True(probe.LockHeldBeforeFirstCommand!.Value);
    }

    [Fact]
    public async Task Ef_read_uncommitted_transaction_is_deferred()
    {
        await using var context = await CreateSchemaAsync();

        await using var transaction = context.Database.BeginTransaction(IsolationLevel.ReadUncommitted);

        // The negative control: ReadUncommitted is the one level Microsoft.Data.Sqlite defers, so no
        // write lock is taken until the transaction writes. This is what the probe reports as "free".
        Assert.False(WriteLockHeldByAnotherConnection());

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Read_then_write_in_one_transaction_commits_without_a_lock_upgrade()
    {
        await using var context = await CreateSchemaAsync();
        context.Rows.Add(new ProbeRow());
        await context.SaveChangesAsync();

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Reading first is what would strand a DEFERRED transaction on a read lock it cannot upgrade —
        // an unretryable SQLITE_BUSY. Because the transaction is already IMMEDIATE, there is nothing to
        // upgrade and the write commits.
        var row = await context.Rows.FirstAsync();
        row.Note = "changed";
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Equal("changed", await context.Rows.Select(r => r.Note).FirstAsync());
    }

    // Asks a second connection for the write lock with SQLite's own busy handler off, so a held lock
    // comes back as SQLITE_BUSY immediately instead of waiting out Microsoft.Data.Sqlite's synchronous
    // ~1 s retry loop. Same raw-handle technique as SqliteBusyRetry in Rask.SQLite.
    private bool WriteLockHeldByAnotherConnection()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        var handle = connection.Handle!;
        raw.sqlite3_busy_timeout(handle, 0);

        var rc = raw.sqlite3_exec(handle, "BEGIN IMMEDIATE;");
        if (rc == raw.SQLITE_OK)
        {
            raw.sqlite3_exec(handle, "ROLLBACK;");
            return false;
        }

        if (rc == raw.SQLITE_BUSY || rc == raw.SQLITE_LOCKED)
        {
            return true;
        }

        throw new InvalidOperationException(
            $"Unexpected result {rc} taking the write lock: {raw.sqlite3_errmsg(handle).utf8_to_string()}");
    }

    private async Task<ProbeDbContext> CreateSchemaAsync()
    {
        var context = new ProbeDbContext(OptionsWith());
        await context.Database.EnsureCreatedAsync();
        return context;
    }

    private DbContextOptions<ProbeDbContext> OptionsWith(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}");

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
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

    private sealed class WriteLockProbeInterceptor(Func<bool> probe) : DbCommandInterceptor
    {
        public bool? LockHeldBeforeFirstCommand { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Record();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record();
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Record();
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record();
            return ValueTask.FromResult(result);
        }

        private void Record() => LockHeldBeforeFirstCommand ??= probe();
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public string? Note { get; set; }
    }
}
