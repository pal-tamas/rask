namespace Rask.SqlServer;

/// <summary>
/// The production defaults <see cref="RaskSqlServerDbContextOptionsExtensions.UseRaskSqlServer(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, string, Action{RaskSqlServerOptions}?)"/>
/// applies.
/// </summary>
/// <remarks>
/// Deliberately not a mirror of <c>RaskPostgresOptions</c>. SQL Server has no server-side statement timeout
/// — the equivalent lever is the <em>client</em> command timeout — and nothing corresponding to
/// <c>idle_in_transaction_session_timeout</c>, so neither is invented here. What it does have, and what
/// PostgreSQL does not need, is <c>XACT_ABORT</c>.
/// </remarks>
public sealed class RaskSqlServerOptions
{
    /// <summary>
    /// How long the <em>client</em> waits for a command before giving up. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// SQL Server has no server-side statement timeout, so this is the only ceiling on a runaway query.
    /// Note the difference from cancelling it server-side: the client stops waiting, and the server may keep
    /// working until it notices the attention signal.
    /// </remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a statement waits for a lock before failing (<c>SET LOCK_TIMEOUT</c>). Defaults to
    /// 10 seconds. <see cref="TimeSpan.Zero"/> leaves the server default (wait forever) alone.
    /// </summary>
    /// <remarks>
    /// The closest analogue to SQLite's <c>busy_timeout</c>. Without it, a statement blocked behind a lock
    /// waits out <see cref="CommandTimeout"/> and surfaces as a command timeout, which sends you looking at
    /// query plans instead of at whatever is holding the lock.
    /// </remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to <c>SET XACT_ABORT ON</c>, so a run-time error rolls the whole transaction back rather than
    /// leaving it open and doomed. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// On by default because the alternative is the quieter bug: with it off, a statement error inside an
    /// explicit transaction leaves that transaction open, holding its locks, until something eventually
    /// rolls it back — which on a web app means the connection goes back to the pool in that state.
    /// </remarks>
    public bool AbortOnError { get; set; } = true;

    /// <summary>
    /// How many times EF Core retries a transient failure (a dropped connection, an Azure SQL failover)
    /// before giving up. Defaults to 6. Set to 0 to disable retrying entirely.
    /// </summary>
    /// <remarks>
    /// Drives SQL Server's own <c>EnableRetryOnFailure</c> rather than a Rask-specific execution strategy:
    /// <c>RaskSqliteExecutionStrategy</c> exists only because SQLite has no built-in one, and the provider's
    /// own list of transient error numbers is exactly the part worth not reimplementing.
    /// </remarks>
    public int MaxRetryCount { get; set; } = 6;

    /// <summary>The ceiling on the exponential backoff between retries. Defaults to 30 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws when the options describe a configuration SQL Server or EF Core would reject.</summary>
    internal void Validate()
    {
        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskSqlServerOptions)}.{nameof(CommandTimeout)} must be positive — SQL Server has no "
                + "server-side statement timeout, so this is the only ceiling on a runaway query.");
        }

        if (LockTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskSqlServerOptions)}.{nameof(LockTimeout)} must not be negative (use TimeSpan.Zero "
                + "to wait indefinitely, which is the server default).");
        }

        if (MaxRetryCount < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskSqlServerOptions)}.{nameof(MaxRetryCount)} must be zero or greater (zero disables retrying).");
        }

        if (MaxRetryDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskSqlServerOptions)}.{nameof(MaxRetryDelay)} must not be negative.");
        }

        // A lock timeout at or above the command timeout can never fire: the client gives up first, and the
        // "waiting for a lock" signal — the whole reason to set it — is lost.
        if (LockTimeout > TimeSpan.Zero && LockTimeout >= CommandTimeout)
        {
            throw new InvalidOperationException(
                $"{nameof(RaskSqlServerOptions)}.{nameof(LockTimeout)} ({LockTimeout}) must be below "
                + $"{nameof(CommandTimeout)} ({CommandTimeout}), otherwise the client times out first and lock "
                + "contention is reported as a slow query.");
        }
    }
}
