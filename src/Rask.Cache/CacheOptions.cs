namespace Rask.Cache;

/// <summary>Options for the cache and its background <see cref="CachePurger{TContext}"/>.</summary>
public sealed class CacheOptions
{
    /// <summary>How often the background purge sweeps expired entries out of the table. Default 5 minutes.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A default sliding expiration applied to entries written without any explicit expiration. <c>null</c> keeps
    /// such entries until they are explicitly removed (they never expire on their own). Default <c>null</c>.
    /// </summary>
    public TimeSpan? DefaultSlidingExpiration { get; set; }

    /// <summary>Validates the option values (called at registration, so a bad value fails fast rather than tearing down the host later).</summary>
    internal void Validate()
    {
        if (PurgeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PurgeInterval), PurgeInterval, "PurgeInterval must be positive.");
        }

        // PeriodicTimer rejects an interval above (uint.MaxValue - 1) ms (~49.7 days). Reject it here so a bad
        // value fails fast at registration instead of throwing later inside the background service and faulting the host.
        if (PurgeInterval.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PurgeInterval), PurgeInterval, "PurgeInterval must be at most ~49 days.");
        }

        if (DefaultSlidingExpiration is { } sliding && sliding <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultSlidingExpiration), DefaultSlidingExpiration, "DefaultSlidingExpiration must be positive when set.");
        }
    }
}
