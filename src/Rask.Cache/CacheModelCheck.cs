using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Cache;

/// <summary>The cache's claim on the application's model, checked once at boot. See #1015.</summary>
/// <remarks>
/// A type of its own rather than a constructed <c>BatteryModelCheck&lt;TContext&gt;</c> so that
/// <c>AddHostedService</c>'s <c>TryAddEnumerable</c> has something to deduplicate on — the
/// <c>AddRaskCache&lt;TContext&gt;</c> overload documents itself as idempotent, and a check registered
/// through a factory delegate would be added again on every call.
/// </remarks>
internal sealed class CacheModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Cache";

    protected override Type Entity => typeof(CacheEntry);

    protected override string MapCall => "AddRaskCache";
}
