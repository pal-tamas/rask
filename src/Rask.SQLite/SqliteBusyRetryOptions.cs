namespace Rask.SQLite;

/// <summary>
/// Controls the non-blocking, fair-interval busy-retry used when a write lock is contended — by
/// <see cref="SqliteConnectionExtensions.InImmediateTransactionAsync{T}"/> /
/// <see cref="ISqlite.InImmediateTransactionAsync{T}"/> on the raw ADO.NET
/// path, and by the Entity Framework Core execution strategy in the <c>Rask.SQLite.EntityFrameworkCore</c>
/// package.
/// </summary>
/// <remarks>
/// A constant-poll-interval busy handler (reference:
/// <a href="https://github.com/rails/rails/pull/51958">https://github.com/rails/rails/pull/51958</a>):
/// a <b>constant poll interval</b> — not exponential backoff, which measured up
/// to 5× worse tail latency — that <b>yields the calling thread</b> between attempts (via
/// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>), giving up once the total wait
/// reaches <see cref="Timeout"/>. On the raw path the write lock is acquired through the native
/// <c>sqlite3</c> handle so that neither SQLite's own busy handler nor Microsoft.Data.Sqlite's
/// synchronous <c>Thread.Sleep</c> retry runs — the wait is genuinely non-blocking. On the Entity
/// Framework Core path Microsoft.Data.Sqlite still owns each individual command execution, so a
/// contended attempt can block for up to its (deliberately lowered) command timeout before this
/// interval-based retry takes over.
/// </remarks>
public sealed class SqliteBusyRetryOptions
{
    /// <summary>
    /// How long to keep retrying a contended write lock before giving up and surfacing
    /// <c>SQLITE_BUSY</c>. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The constant wait between retries — the "fair interval". Defaults to 1 millisecond.
    /// Keep it small: a uniform interval treats every waiter
    /// equally and minimises tail latency.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>Throws <see cref="InvalidOperationException"/> if any configured value is out of range.</summary>
    internal void Validate()
    {
        if (Timeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Timeout)} must not be negative (was {Timeout}).");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(PollInterval)} must be positive (was {PollInterval}).");
        }

        // Task.Delay takes a 32-bit millisecond count; a larger interval would overflow.
        if (PollInterval.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(PollInterval)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)} (was {PollInterval}).");
        }
    }
}
