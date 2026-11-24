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

    /// <summary>
    /// Whether a query that includes several collections runs as one statement per collection rather than one
    /// joined statement. Defaults to <see langword="false"/>, EF Core's own default.
    /// </summary>
    /// <remarks>
    /// One joined statement repeats the parent row for every combination of its children, which for two wide
    /// collections is a lot of rows to send and discard. Splitting trades that for extra round trips, and for the
    /// chance that a row changes between them. A single query still chooses for itself with
    /// <c>AsSplitQuery()</c> or <c>AsSingleQuery()</c>.
    /// </remarks>
    public bool SplitQueries { get; set; }

    /// <summary>
    /// The compatibility level EF Core generates SQL for — <c>170</c> for SQL Server 2025, <c>160</c> for 2022. Defaults
    /// to <see langword="null"/>: read from the server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Left unset, Rask asks the server once per connection string — the database's own level, or, before the database
    /// exists, the level its server version gives a new one — and EF Core keeps its own default when the server cannot
    /// be reached or is Azure SQL Database, which reports no version worth mapping.
    /// </para>
    /// <para>
    /// Set it to pin the level without asking: SQL written for a level above the database's fails at run time, and one
    /// below it gives up the newer translations.
    /// </para>
    /// </remarks>
    public int? CompatibilityLevel { get; set; }

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

        // Every SQL Server level is a multiple of ten (100, 150, 170): a 17 is a version typed where a level belongs.
        if (CompatibilityLevel is { } level && (level is <= 0 or > 990 || level % 10 != 0))
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(CompatibilityLevel)} must be a SQL Server compatibility level such as "
                + $"170 or 160 (was {CompatibilityLevel}). Leave it unset to read it from the server.");
        }

        Retry.Validate();
    }
}
