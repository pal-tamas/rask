using System.ComponentModel;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache;

/// <summary>
///     How long an entry lives, as the steps <c>.For(10.Minutes)</c>, <c>.Sliding(20.Minutes)</c> and
///     <c>.Until(midnight)</c> set it. With none of them, the entry follows <see cref="CacheOptions.DefaultSlidingExpiration" />,
///     and never expires when that is unset.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct CacheLifetime(TimeSpan? For, TimeSpan? Sliding, DateTimeOffset? Until)
{
    internal DistributedCacheEntryOptions ToEntryOptions() => new()
    {
        AbsoluteExpirationRelativeToNow = For,
        SlidingExpiration = Sliding,
        AbsoluteExpiration = Until,
    };

    internal static TimeSpan Positive(TimeSpan span, string step) =>
        span > TimeSpan.Zero
            ? span
            : throw new ArgumentOutOfRangeException(
                nameof(span), span, $"An entry has to live for some time — .{step}(…) takes a positive duration, such as 10.Minutes.");
}
