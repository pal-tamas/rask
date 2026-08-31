using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.SQLite;

/// <summary>
/// An Entity Framework Core <see cref="ExecutionStrategy"/> that retries a contended write
/// (<c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c>) using a fair interval: a <b>constant</b>
/// <see cref="SqliteBusyRetryOptions.PollInterval"/> — not exponential backoff — awaited between
/// attempts (so the thread is freed while waiting), giving up after
/// <see cref="SqliteBusyRetryOptions.Timeout"/>. Registered for you by
/// <see cref="RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{SqliteOptions}?)"/>
/// when you pass <c>configureRetry</c>.
/// </summary>
/// <remarks>
/// Unlike the raw-ADO path (<see cref="SqliteConnectionExtensions.InImmediateTransactionAsync{T}"/>),
/// EF Core issues every command through Microsoft.Data.Sqlite, whose own synchronous busy-retry can block
/// a thread for up to its command timeout before <c>SQLITE_BUSY</c> reaches this strategy — which is why
/// <c>UseRaskSqlite</c> lowers that timeout. As with any retrying <see cref="ExecutionStrategy"/>, a
/// user-initiated transaction must be wrapped in <see cref="IExecutionStrategy.ExecuteAsync{TState,TResult}"/>.
/// </remarks>
public sealed class RaskSqliteExecutionStrategy : ExecutionStrategy
{
    // SqliteException.SqliteErrorCode primary result codes (SQLitePCLRaw's raw.SQLITE_BUSY / SQLITE_LOCKED),
    // inlined to avoid taking a direct dependency on SQLitePCL from the EF Core package.
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;

    private readonly SqliteBusyRetryOptions _retry;
    private long _startTimestamp;
    private bool _started;

    /// <summary>Creates the strategy for <paramref name="dependencies"/> using <paramref name="retry"/>.</summary>
    public RaskSqliteExecutionStrategy(ExecutionStrategyDependencies dependencies, SqliteBusyRetryOptions retry)
        : base(dependencies, maxRetryCount: int.MaxValue, maxRetryDelay: retry?.Timeout ?? TimeSpan.Zero)
    {
        ArgumentNullException.ThrowIfNull(retry);
        _retry = retry;
    }

    /// <inheritdoc/>
    protected override bool ShouldRetryOn(Exception exception)
    {
        // EF wraps the provider error (e.g. SaveChanges → DbUpdateException); walk the chain.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqliteException sqlite &&
                sqlite.SqliteErrorCode is SqliteBusy or SqliteLocked)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    protected override void OnFirstExecution()
    {
        base.OnFirstExecution();

        // EF hands one DbContext a single strategy instance for its whole lifetime, so the clock must be
        // released at the start of every operation. Without this, a context that outlives Timeout carries the
        // first contention's start timestamp into its next SaveChanges and gives up without a single retry.
        _started = false;
    }

    /// <inheritdoc/>
    protected override TimeSpan? GetNextDelay(Exception lastException)
    {
        // Start the clock on this operation's first contention, then poll at the constant fair interval until
        // the total wait reaches Timeout (returning null tells the base strategy to give up).
        if (!_started)
        {
            _started = true;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        return Stopwatch.GetElapsedTime(_startTimestamp) >= _retry.Timeout
            ? null
            : _retry.PollInterval;
    }
}
