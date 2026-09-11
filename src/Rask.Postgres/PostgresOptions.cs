namespace Rask.Postgres;

/// <summary>
/// The production defaults <c>UseRaskPostgres(...)</c> applies: the per-session timeouts that keep one bad
/// query from holding a connection (or a lock) open forever, and the transient-failure retry policy.
/// </summary>
/// <remarks>
/// The SQLite counterpart is <c>SqliteOptions</c>, and the parallel is deliberate: both are "the settings a
/// production app wants on every connection, which nobody remembers to set". The contents differ because the
/// failure modes do — SQLite's are about a single writer contending for one file, PostgreSQL's are about a
/// query or an idle transaction pinning a server-side connection.
/// </remarks>
public sealed class PostgresOptions
{
    /// <summary>
    /// Cancels any single statement running longer than this (PostgreSQL <c>statement_timeout</c>). Defaults
    /// to 30 seconds. <see cref="TimeSpan.Zero"/> leaves the server's own setting alone — PostgreSQL's default
    /// is no limit, so a runaway query runs until the client disconnects.
    /// </summary>
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a statement waits for a lock before failing (PostgreSQL <c>lock_timeout</c>). Defaults to 10
    /// seconds. This is the closest analogue to SQLite's <c>busy_timeout</c>: without it, a statement blocked
    /// behind a lock waits for <see cref="StatementTimeout"/> and reports the timeout as if the query itself
    /// were slow, which sends you looking in the wrong place.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Ends a session that holds a transaction open without doing work for this long (PostgreSQL
    /// <c>idle_in_transaction_session_timeout</c>). Defaults to 1 minute. An idle-in-transaction session keeps
    /// its locks and blocks <c>VACUUM</c> from reclaiming dead rows, so leaking one is how a healthy database
    /// quietly bloats.
    /// </summary>
    public TimeSpan IdleInTransactionSessionTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How EF Core retries a transient failure — a dropped connection, a failover.</summary>
    public PostgresRetryOptions Retry { get; } = new();

    /// <summary>Throws when the options describe a configuration PostgreSQL or EF Core would reject.</summary>
    internal void Validate()
    {
        ThrowIfNegative(StatementTimeout, nameof(StatementTimeout));
        ThrowIfNegative(LockTimeout, nameof(LockTimeout));
        ThrowIfNegative(IdleInTransactionSessionTimeout, nameof(IdleInTransactionSessionTimeout));

        // A lock timeout at or above the statement timeout can never fire: the statement is cancelled first,
        // and the "waiting for a lock" signal — the whole reason to set it — is lost.
        if (StatementTimeout > TimeSpan.Zero && LockTimeout >= StatementTimeout)
        {
            throw new InvalidOperationException(
                $"{nameof(PostgresOptions)}.{nameof(LockTimeout)} ({LockTimeout}) must be below "
                + $"{nameof(StatementTimeout)} ({StatementTimeout}), otherwise the statement timeout always "
                + "fires first and lock contention is reported as a slow query.");
        }

        Retry.Validate();
    }

    private static void ThrowIfNegative(TimeSpan value, string name)
    {
        if (value < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(PostgresOptions)}.{name} must not be negative (use TimeSpan.Zero to leave it to the server).");
        }

        // PostgreSQL holds these as a 32-bit millisecond count. A larger value would validate here and then fail
        // every connection open in production with "outside the valid range", so it is refused at startup.
        if (value > TimeSpan.Zero && PostgresSessionSettings.Milliseconds(value) > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(PostgresOptions)}.{name} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)} "
                + $"(was {value}) — PostgreSQL stores it as a 32-bit millisecond count.");
        }
    }
}
