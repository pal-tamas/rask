using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Root-causing a rare "escaped SQLITE_BUSY" seen by the load harness: what surfaces when a SaveChanges that
// is riding out a contended lock gets CANCELLED mid-retry? If it surfaces as SqliteException(BUSY) rather
// than as cancellation, then anything that cancels in-flight work (a load harness's measurement deadline, a
// web request's aborted CancellationToken) sees a "database is locked" that never actually escaped the retry.
public sealed class RaskSqliteCancelledRetryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-cancel-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    [Fact]
    public async Task Cancelling_a_retrying_SaveChanges_surfaces_as_cancellation_not_as_busy()
    {
        // A budget far longer than the test: the retry can only be ended by the cancellation, never by
        // running out of time. Any SQLITE_BUSY here therefore did NOT exhaust the budget.
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite(ConnectionString, configureRetry: r => r.Timeout = TimeSpan.FromMinutes(5))
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.EnsureCreatedAsync();

        // Hold the write lock for the whole test, so SaveChanges is stuck retrying.
        await using var holder = new SqliteConnection(ConnectionString);
        await holder.OpenAsync();
        using var holderTx = holder.BeginImmediate();

        using var cts = new CancellationTokenSource();
        context.Rows.Add(new ProbeRow());
        var save = context.SaveChangesAsync(cts.Token);

        // Let it get well into the retry loop, then cancel the way a deadline or an aborted request would.
        await Task.Delay(1500);
        await cts.CancelAsync();

        var exception = await Record.ExceptionAsync(() => save);

        Assert.NotNull(exception);

        // Cancellation must be recognisable the cheap way — by the type of the exception that is actually
        // thrown. A caller that reaches for SqliteException first (the obvious way to detect "database is
        // locked") must not find one ahead of the cancellation in the chain, or it will report a lock error
        // for work that was merely cancelled.
        Assert.True(
            exception is OperationCanceledException,
            $"expected the thrown exception to BE cancellation, got {Describe(exception)}");
    }

    private static bool IsCancellation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            parts.Add(current is SqliteException sqlite
                ? $"{current.GetType().Name}(rc={sqlite.SqliteErrorCode}, ext={sqlite.SqliteExtendedErrorCode})"
                : current.GetType().Name);
        }

        return string.Join(" -> ", parts);
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
