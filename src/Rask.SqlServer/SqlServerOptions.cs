namespace Rask.SqlServer;

/// <summary>
/// The production defaults <c>UseRaskSqlServer(...)</c> applies.
/// </summary>
/// <remarks>
/// Deliberately not a mirror of <c>PostgresOptions</c>. SQL Server has no server-side statement timeout — the
/// equivalent lever is the <em>client</em> command timeout — and nothing corresponding to
/// <c>idle_in_transaction_session_timeout</c>, so neither is invented here. What it does have, and PostgreSQL
/// does not need, is <c>XACT_ABORT</c>. The two packages match in shape, not in knobs.
/// </remarks>
public sealed class SqlServerOptions
{
    /// <summary>
    /// How long the <em>client</em> waits for a command before giving up. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// SQL Server has no server-side statement timeout, so this is the only ceiling on a runaway query. It is sent
    /// in whole seconds, rounded up, because SqlClient reads <c>0</c> as "wait forever".
    /// </remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a statement waits for a lock before failing (<c>SET LOCK_TIMEOUT</c>). Defaults to 10 seconds.
    /// <see cref="TimeSpan.Zero"/> leaves the server default — wait indefinitely — alone.
    /// </summary>
    /// <remarks>
    /// The closest analogue to SQLite's <c>busy_timeout</c>. Without it, a statement blocked behind a lock waits
    /// out <see cref="CommandTimeout"/> and surfaces as a command timeout, which sends you reading query plans
    /// instead of finding whatever holds the lock.
    /// </remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether to <c>SET XACT_ABORT ON</c>, so a run-time error rolls the whole transaction back rather than
    /// leaving it open and doomed. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// On by default because the alternative is the quieter bug: with it off, a statement error inside an explicit
    /// transaction leaves that transaction open and holding its locks until something rolls it back — which in a
    /// web app means the connection goes back to the pool in that state.
    /// </remarks>
    public bool AbortOnError { get; set; } = true;

    /// <summary>How EF Core retries a transient failure — a dropped connection, an Azure SQL failover.</summary>
    public SqlServerRetryOptions Retry { get; } = new();

    /// <summary>Throws when the options describe a configuration SQL Server or EF Core would reject.</summary>
    internal void Validate()
    {
        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(CommandTimeout)} must be positive (was {CommandTimeout}) — SQL "
                + "Server has no server-side statement timeout, so this is the only ceiling on a runaway query.");
        }

        if (SqlServerSessionSettings.Seconds(CommandTimeout) > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(CommandTimeout)} must be at most {TimeSpan.FromSeconds(int.MaxValue)} "
                + $"(was {CommandTimeout}) — SqlClient takes it as a 32-bit second count.");
        }

        if (LockTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(LockTimeout)} must not be negative (was {LockTimeout}). Use "
                + "TimeSpan.Zero to wait indefinitely, which is the server default.");
        }

        if (LockTimeout > TimeSpan.Zero && SqlServerSessionSettings.Milliseconds(LockTimeout) > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(LockTimeout)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)} "
                + $"(was {LockTimeout}) — SET LOCK_TIMEOUT takes a 32-bit millisecond count.");
        }

        // A lock timeout at or above the command timeout can never fire: the client gives up first, and the
        // "waiting for a lock" signal — the whole reason to set it — is lost.
        if (LockTimeout > TimeSpan.Zero && LockTimeout >= CommandTimeout)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(LockTimeout)} ({LockTimeout}) must be below "
                + $"{nameof(CommandTimeout)} ({CommandTimeout}), otherwise the client times out first and lock "
                + "contention is reported as a slow query.");
        }

        Retry.Validate();
    }
}
