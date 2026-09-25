using Microsoft.Extensions.Options;

namespace Rask.Caching;

/// <summary>
/// Checks <see cref="CacheOptions"/> once <c>Rask:Cache</c> and the callback have applied — at host start, so a
/// bad value fails fast, naming its key, rather than tearing down the host later.
/// </summary>
internal sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.PurgeInterval <= TimeSpan.Zero)
        {
            failures.Add("Rask:Cache:PurgeInterval must be positive.");
        }
        else if (options.PurgeInterval.TotalMilliseconds > uint.MaxValue - 1)
        {
            // PeriodicTimer rejects an interval above (uint.MaxValue - 1) ms (~49.7 days). Rejected here so a bad
            // value fails fast at start instead of throwing later inside the background service and faulting the host.
            failures.Add("Rask:Cache:PurgeInterval must be at most ~49 days.");
        }

        if (options.DefaultSlidingExpiration is { } sliding && sliding <= TimeSpan.Zero)
        {
            failures.Add("Rask:Cache:DefaultSlidingExpiration must be positive when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
