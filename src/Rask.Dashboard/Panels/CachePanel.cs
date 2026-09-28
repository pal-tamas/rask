using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Caching;

namespace Rask.Dashboard.Panels;

/// <summary>
/// The cache is not a queue — no attempts, no dead letters — so it gets its own small panel rather than
/// being forced through <see cref="IQueuePanel"/>.
/// </summary>
internal sealed class CachePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services) : ICachePanelReader
    where TContext : DbContext
{
    private readonly CacheOptions? _options = services.GetService<CacheOptions>();
    private bool? _mapped;

    public bool IsAvailable => _options is not null && IsMapped();

    /// <summary>Entry count, total stored bytes, and how many are expired but not yet swept.</summary>
    public async Task<CacheStats> StatsAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return default;
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var entries = db.Set<CacheEntry>();

        return new CacheStats(
            Entries: await entries.CountAsync(cancellationToken).ConfigureAwait(false),
            // SUM over an empty table is NULL in SQL, hence the nullable projection rather than a plain Sum.
            Bytes: await entries.SumAsync(e => (long?)e.Value.Length, cancellationToken).ConfigureAwait(false) ?? 0,
            Expired: await entries.CountAsync(e => e.ExpiresAt <= now, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>One page of keys, soonest to expire first — the ones about to vanish are the interesting ones.</summary>
    public async Task<(IReadOnlyList<CacheKeyRow> Rows, int Total)> PageAsync(
        string? search, int skip, int take, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ([], 0);
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var query = db.Set<CacheEntry>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(e => e.Key.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderBy(e => e.ExpiresAt)
            .Skip(skip)
            .Take(take)
            .Select(e => new CacheKeyRow(e.Key, e.Value.Length, e.CreatedAt, e.ExpiresAt, e.SlidingSeconds))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (rows, total);
    }

    /// <inheritdoc/>
    public async Task<int> EvictAsync(string key, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return 0;
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return await db.Set<CacheEntry>()
            .Where(e => e.Key == key)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return 0;
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        return await db.Set<CacheEntry>().ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool IsMapped()
    {
        if (_mapped is { } known)
        {
            return known;
        }

        using var db = contextFactory.CreateDbContext();
        var mapped = db.Model.FindEntityType(typeof(CacheEntry)) is not null;
        _mapped = mapped;
        return mapped;
    }
}
