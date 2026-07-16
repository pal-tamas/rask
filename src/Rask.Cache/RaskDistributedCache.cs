using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Rask.Cache;

/// <summary>
/// An <see cref="IDistributedCache"/> backed by a <see cref="CacheEntry"/> table on the app's own database —
/// no broker, no Redis. Honors absolute and sliding expirations; a read renews a sliding entry, and an entry
/// found past its deadline is treated as a miss and evicted lazily. Reads and writes go through the app's
/// <see cref="IDbContextFactory{TContext}"/> so each operation gets a fresh short-lived context.
/// </summary>
/// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the cache table.</typeparam>
public sealed class RaskDistributedCache<TContext>(
    IDbContextFactory<TContext> contextFactory,
    CacheOptions options,
    TimeProvider timeProvider,
    ILogger<RaskDistributedCache<TContext>> logger) : IDistributedCache
    where TContext : DbContext
{
    /// <inheritdoc/>
    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await using var db = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);
        // No-tracking read: the mutations below use set-based ExecuteDelete/ExecuteUpdate, so nothing is saved
        // through the change tracker — which keeps concurrent reads of the same key from racing on a tracked save.
        var entry = await db.Set<CacheEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (entry.ExpiresAt <= now)
        {
            // Past its deadline: a miss. Evict lazily so the row doesn't wait for the next purge sweep. The
            // ExpiresAt guard avoids deleting a row a concurrent writer just refreshed between this read and now.
            // Best-effort: a failed evict just leaves the row for the purger — it must not make the read throw.
            await BestEffortWriteAsync(
                db.Set<CacheEntry>().Where(e => e.Key == key && e.ExpiresAt <= now).ExecuteDeleteAsync(token),
                token).ConfigureAwait(false);
            return null;
        }

        // A read renews the sliding window (capped by any absolute deadline).
        if (entry.SlidingSeconds is { } seconds)
        {
            var renewed = now.AddSeconds(seconds);
            if (entry.AbsoluteExpiration is { } absolute && renewed > absolute)
            {
                renewed = absolute;
            }

            if (renewed != entry.ExpiresAt)
            {
                // Best-effort: losing a renewal under write contention (SQLITE_BUSY) is acceptable — a read must
                // never throw for it. The entry is still valid; it may just expire a little sooner than it could.
                await BestEffortWriteAsync(
                    db.Set<CacheEntry>().Where(e => e.Key == key)
                        .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExpiresAt, renewed), token),
                    token).ConfigureAwait(false);
            }
        }

        return entry.Value;
    }

    // Runs a maintenance write issued during a read (lazy eviction / sliding renewal). These are optimizations,
    // not the read's result, so a transient database error (e.g. SQLITE_BUSY) is logged and swallowed rather
    // than thrown to a caller who only asked to read.
    private async Task BestEffortWriteAsync(Task write, CancellationToken token)
    {
        try
        {
            await write.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transient DB error on a best-effort maintenance write must not fault the read.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogDebug(ex, "Best-effort cache maintenance write failed; ignoring.");
        }
    }

    /// <inheritdoc/>
    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public Task RefreshAsync(string key, CancellationToken token = default) =>
        // A read renews the sliding window; the returned value is discarded.
        GetAsync(key, token);

    /// <inheritdoc/>
    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        await using var db = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);
        await db.Set<CacheEntry>().Where(e => e.Key == key).ExecuteDeleteAsync(token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        SetAsync(key, value, options).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (absolute, sliding, expiresAt) = Resolve(options, now);

        await using var db = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);
        var entry = await db.Set<CacheEntry>().FirstOrDefaultAsync(e => e.Key == key, token).ConfigureAwait(false);
        if (entry is not null)
        {
            entry.Value = value;
            entry.AbsoluteExpiration = absolute;
            entry.SlidingSeconds = sliding?.TotalSeconds;
            entry.ExpiresAt = expiresAt;
            entry.CreatedAt = now;
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            return;
        }

        db.Set<CacheEntry>().Add(new CacheEntry
        {
            Key = key,
            Value = value,
            AbsoluteExpiration = absolute,
            SlidingSeconds = sliding?.TotalSeconds,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // A concurrent writer (a cache stampede on a cold key) may have inserted this key first — last write
            // wins: clear the failed insert and update the now-existing row instead of surfacing the primary-key
            // clash. If no row matches, the failure was NOT a duplicate-key conflict (some other write error), so
            // rethrow rather than swallow it — a Set must not silently lose the value.
            db.ChangeTracker.Clear();
            var updated = await db.Set<CacheEntry>()
                .Where(e => e.Key == key)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(e => e.Value, value)
                        .SetProperty(e => e.AbsoluteExpiration, absolute)
                        .SetProperty(e => e.SlidingSeconds, sliding == null ? (double?)null : sliding.Value.TotalSeconds)
                        .SetProperty(e => e.ExpiresAt, expiresAt)
                        .SetProperty(e => e.CreatedAt, now),
                    token)
                .ConfigureAwait(false);
            if (updated == 0)
            {
                throw;
            }
        }
    }

    // Resolve the entry-options (absolute, relative-to-now, sliding) plus this instance's DefaultSlidingExpiration
    // into a concrete absolute deadline, sliding window, and the effective next-expiry timestamp.
    private (DateTime? Absolute, TimeSpan? Sliding, DateTime ExpiresAt) Resolve(DistributedCacheEntryOptions entryOptions, DateTime now)
    {
        DateTime? absolute = entryOptions.AbsoluteExpiration?.UtcDateTime;
        if (entryOptions.AbsoluteExpirationRelativeToNow is { } relative)
        {
            var relativeAbsolute = now + relative;
            absolute = absolute is { } existing && existing < relativeAbsolute ? existing : relativeAbsolute;
        }

        var sliding = entryOptions.SlidingExpiration;
        // Fall back to the configured default sliding expiration ONLY when the caller specified no expiration at
        // all — an entry with an explicit absolute deadline must keep it, not have it shortened by the default.
        if (sliding is null && absolute is null)
        {
            sliding = options.DefaultSlidingExpiration;
        }

        DateTime expiresAt;
        if (sliding is { } window)
        {
            expiresAt = now + window;
            if (absolute is { } deadline && deadline < expiresAt)
            {
                expiresAt = deadline;
            }
        }
        else if (absolute is { } deadline)
        {
            expiresAt = deadline;
        }
        else
        {
            expiresAt = DateTime.MaxValue; // No expiration was requested: keep until explicitly removed.
        }

        return (absolute, sliding, expiresAt);
    }
}
