namespace Rask.MySql;

/// <summary>
/// The production defaults <c>UseRaskMySql(...)</c> applies.
/// </summary>
/// <remarks>
/// Deliberately not a mirror of the PostgreSQL or SQL Server options. MySQL's statement timeout,
/// <c>max_execution_time</c>, applies to read-only <c>SELECT</c> statements only, so the ceiling on a runaway
/// write is the client <see cref="CommandTimeout"/>; and its lock wait, <c>innodb_lock_wait_timeout</c>, is whole
/// seconds. The packages match in shape, not in knobs.
/// </remarks>
public sealed class MySqlOptions
{
    /// <summary>
    /// How long the <em>client</em> waits for a command before giving up. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    /// The only ceiling on a runaway write, because MySQL's server-side statement timeout covers <c>SELECT</c> alone.
    /// Sent in whole seconds, rounded up, because the driver reads <c>0</c> as "wait forever".
    /// </remarks>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Stops a read-only <c>SELECT</c> running longer than this (MySQL <c>max_execution_time</c>). Defaults to 30
    /// seconds. <see cref="TimeSpan.Zero"/> leaves the server's own setting alone.
    /// </summary>
    /// <remarks>
    /// MySQL applies it to read-only <c>SELECT</c> statements only — an <c>UPDATE</c> or <c>INSERT</c> is bounded by
    /// <see cref="CommandTimeout"/> instead. Sent in whole milliseconds, rounded up, because <c>0</c> means
    /// "no limit".
    /// </remarks>
    public TimeSpan StatementTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a statement waits for a row lock before failing (InnoDB <c>innodb_lock_wait_timeout</c>). Defaults to
    /// 10 seconds. <see cref="TimeSpan.Zero"/> leaves the server's own setting — 50 seconds by default — alone.
    /// </summary>
    /// <remarks>
    /// The closest analogue to SQLite's <c>busy_timeout</c>. Without it a write blocked behind a lock waits 50
    /// seconds, outlasting the command timeout, and surfaces as a slow query. Sent in whole seconds, rounded up:
    /// MySQL takes nothing finer.
    /// </remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How EF Core retries a transient failure — a deadlock victim, a dropped connection.</summary>
    public MySqlRetryOptions Retry { get; } = new();

    /// <summary>Throws when the options describe a configuration MySQL or EF Core would reject.</summary>
    internal void Validate()
    {
        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(CommandTimeout)} must be positive (was {CommandTimeout}) — MySQL's own "
                + "statement timeout covers SELECT only, so this is the only ceiling on a runaway write.");
        }

        if (MySqlSessionSettings.Seconds(CommandTimeout) > MySqlSessionSettings.MaxCommandTimeoutSeconds)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(CommandTimeout)} must be at most "
                + $"{TimeSpan.FromSeconds(MySqlSessionSettings.MaxCommandTimeoutSeconds)} (was {CommandTimeout}) — "
                + "MySql.Data silently cuts a longer command timeout down to that.");
        }

        if (StatementTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(StatementTimeout)} must not be negative (was {StatementTimeout}). Use "
                + "TimeSpan.Zero to leave it to the server.");
        }

        if (StatementTimeout > TimeSpan.Zero && MySqlSessionSettings.Milliseconds(StatementTimeout) > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(StatementTimeout)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)} "
                + $"(was {StatementTimeout}).");
        }

        if (LockTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(LockTimeout)} must not be negative (was {LockTimeout}). Use "
                + "TimeSpan.Zero to leave it to the server.");
        }

        if (LockTimeout > TimeSpan.Zero && MySqlSessionSettings.Seconds(LockTimeout) > MySqlSessionSettings.MaxLockWaitSeconds)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(LockTimeout)} must be at most "
                + $"{TimeSpan.FromSeconds(MySqlSessionSettings.MaxLockWaitSeconds)} (was {LockTimeout}) — the largest "
                + "innodb_lock_wait_timeout MySQL accepts.");
        }

        // A lock wait at or above the command timeout can never fire: the client gives up first, and the
        // "waiting for a lock" signal — the whole reason to set it — is lost. Compared in the whole seconds both are
        // sent as, so 9.5s against 10s — both sent as 10 — is refused too.
        if (LockTimeout > TimeSpan.Zero
            && MySqlSessionSettings.Seconds(LockTimeout) >= MySqlSessionSettings.Seconds(CommandTimeout))
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlOptions)}.{nameof(LockTimeout)} ({LockTimeout}) must be below "
                + $"{nameof(CommandTimeout)} ({CommandTimeout}) once both are rounded up to whole seconds, otherwise "
                + "the client times out first and lock contention is reported as a slow query.");
        }

        Retry.Validate();
    }
}
