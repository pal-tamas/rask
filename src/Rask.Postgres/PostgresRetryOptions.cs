namespace Rask.Postgres;

/// <summary>
/// How EF Core retries a transient PostgreSQL failure — a dropped connection, a failover, a serialization
/// conflict — before giving up.
/// </summary>
/// <remarks>
/// This drives Npgsql's own <c>EnableRetryOnFailure</c> rather than a Rask-specific execution strategy.
/// <c>RaskSqliteExecutionStrategy</c> exists only because SQLite has no built-in one; reimplementing a solved
/// problem here would be strictly worse than the provider's own list of transient error codes.
/// </remarks>
public sealed class PostgresRetryOptions
{
    /// <summary>Whether transient failures are retried. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many times a failed operation is retried before the error surfaces. Defaults to 6.</summary>
    public int MaxCount { get; set; } = 6;

    /// <summary>The ceiling on the exponential backoff between retries. Defaults to 30 seconds.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws when the retry policy is out of range.</summary>
    internal void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (MaxCount < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(PostgresOptions)}.{nameof(PostgresOptions.Retry)}.{nameof(MaxCount)} must be at least 1 "
                + $"(was {MaxCount}). Turn retrying off with o.Retry.Enabled = false.");
        }

        if (MaxDelay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(PostgresOptions)}.{nameof(PostgresOptions.Retry)}.{nameof(MaxDelay)} must be positive "
                + $"(was {MaxDelay}).");
        }
    }
}
