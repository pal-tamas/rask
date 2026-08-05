namespace Rask.Postgres;

/// <summary>
/// The production defaults <see cref="RaskPostgresDbContextOptionsExtensions.UseRaskPostgres(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{RaskPostgresOptions}?)"/>
/// applies: the per-session timeouts that keep one bad query from holding a connection (or a lock) open
/// forever, and the transient-failure retry policy.
/// </summary>
/// <remarks>
/// The SQLite counterpart is <c>SqlitePragmaOptions</c>, and the parallel is deliberate: both are "the
/// settings a production app wants on every connection, which nobody remembers to set". The contents
/// differ because the failure modes do — SQLite's are about a single writer contending for one file,
/// PostgreSQL's are about a query or an idle transaction pinning a server-side connection.
/// </remarks>
public sealed class RaskPostgresOptions
{
    /// <summary>
    /// Cancels any single statement running longer than this (PostgreSQL <c>statement_timeout</c>).
    /// Defaults to 30 seconds. <see cref="TimeSpan.Zero"/> disables it, which is PostgreSQL's own default
    /// and means a runaway query runs until the client disconnects.
    /// </summary>
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a statement waits for a lock before failing (PostgreSQL <c>lock_timeout</c>). Defaults to
    /// 10 seconds. This is the closest analogue to SQLite's <c>busy_timeout</c>: without it, a statement
    /// blocked behind a lock waits for <see cref="StatementTimeout"/> and reports the timeout as if the
    /// query itself were slow, which sends you looking in the wrong place.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Kills a session that holds a transaction open without doing work for this long (PostgreSQL
    /// <c>idle_in_transaction_session_timeout</c>). Defaults to 1 minute. An idle-in-transaction session
    /// keeps its locks and blocks <c>VACUUM</c> from reclaiming dead rows, so leaking one is how a healthy
    /// database quietly bloats.
    /// </summary>
    public TimeSpan IdleInTransactionSessionTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How many times EF Core retries a transient failure (a dropped connection, a failover) before giving
    /// up. Defaults to 6. Set to 0 to disable retrying entirely.
    /// </summary>
    /// <remarks>
    /// This drives Npgsql's own <c>EnableRetryOnFailure</c> rather than a Rask-specific execution strategy:
    /// <c>RaskSqliteExecutionStrategy</c> exists only because SQLite has no built-in one, and reimplementing
    /// a solved problem here would be strictly worse than the provider's own list of transient error codes.
    /// </remarks>
    public int MaxRetryCount { get; set; } = 6;

    /// <summary>The ceiling on the exponential backoff between retries. Defaults to 30 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws when the options describe a configuration PostgreSQL or EF Core would reject.</summary>
    internal void Validate()
    {
        ThrowIfNegative(StatementTimeout, nameof(StatementTimeout));
        ThrowIfNegative(LockTimeout, nameof(LockTimeout));
        ThrowIfNegative(IdleInTransactionSessionTimeout, nameof(IdleInTransactionSessionTimeout));

        if (MaxRetryCount < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskPostgresOptions)}.{nameof(MaxRetryCount)} must be zero or greater (zero disables retrying).");
        }

        if (MaxRetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskPostgresOptions)}.{nameof(MaxRetryDelay)} must not be negative.");
        }

        // A lock timeout at or above the statement timeout can never fire: the statement is cancelled first,
        // and the "waiting for a lock" signal — the whole reason to set it — is lost.
        if (StatementTimeout > TimeSpan.Zero && LockTimeout >= StatementTimeout)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskPostgresOptions)}.{nameof(LockTimeout)} ({LockTimeout}) must be below "
                + $"{nameof(StatementTimeout)} ({StatementTimeout}), otherwise the statement timeout always "
                + "fires first and lock contention is reported as a slow query.");
        }
    }

    private static void ThrowIfNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskPostgresOptions)}.{name} must not be negative (use TimeSpan.Zero to disable it).");
        }
    }
}
