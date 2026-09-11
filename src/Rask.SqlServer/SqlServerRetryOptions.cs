namespace Rask.SqlServer;

/// <summary>
/// How EF Core retries a transient SQL Server failure — a dropped connection, a deadlock victim, an Azure SQL
/// failover — before giving up.
/// </summary>
/// <remarks>
/// This drives SQL Server's own <c>EnableRetryOnFailure</c> rather than a Rask-specific execution strategy: it
/// already knows the transient error numbers, the Azure SQL failover set included, and that list is the part
/// worth not reimplementing.
/// </remarks>
public sealed class SqlServerRetryOptions
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
                $"{nameof(SqlServerOptions)}.{nameof(SqlServerOptions.Retry)}.{nameof(MaxCount)} must be at least 1 "
                + $"(was {MaxCount}). Turn retrying off with o.Retry.Enabled = false.");
        }

        if (MaxDelay <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(SqlServerOptions)}.{nameof(SqlServerOptions.Retry)}.{nameof(MaxDelay)} must be positive "
                + $"(was {MaxDelay}).");
        }
    }
}
