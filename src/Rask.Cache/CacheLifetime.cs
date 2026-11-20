using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Caching;

/// <summary>
///     How long an entry lives, as the steps <c>.For(10.Minutes)</c>, <c>.Sliding(20.Minutes)</c>, <c>.Until(midnight)</c>
///     and <c>.Forever()</c> set it. With none of them, the entry follows <see cref="CacheOptions.DefaultSlidingExpiration" />
///     when that is set, and lives <see cref="CacheOptions.DefaultLifetime" /> (24 hours) when it is not.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[StructLayout(LayoutKind.Auto)]
public readonly record struct CacheLifetime(TimeSpan? For, TimeSpan? Sliding, DateTimeOffset? Until)
{
    /// <summary>Kept until it is forgotten: <c>.Forever()</c>.</summary>
    public bool Forever { get; init; }

    /// <summary>Whether no step said how long it lives.</summary>
    internal bool IsUnsaid => For is null && Sliding is null && Until is null && !Forever;

    /// <summary>
    ///     This lifetime, or — when no step said one — <paramref name="lifetime" /> from now, unless the store has a sliding
    ///     default of its own to apply.
    /// </summary>
    internal CacheLifetime OrDefault(TimeSpan lifetime, bool storeSlides) =>
        IsUnsaid && !storeSlides ? this with { For = lifetime } : this;

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
