using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Rask.Data;

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
    // One cache, isolated per tenant by the KEY rather than by a query filter.
    //
    // A filter is the wrong tool here twice over. The purger has to sweep every tenant's expired rows, and a
    // filter would hide them from it. And ASP.NET's own session and output caching write through this
    // interface on requests that have no signed-in user at all — a partitioned table is stamped from the
    // ambient tenant and refused without one, so an anonymous page with output caching would throw.
    //
    // Scoping the key instead makes the isolation exact rather than enforced: a tenant cannot form another
    // tenant's key, so there is nothing to get wrong. With no tenant in flight the key is untouched, which is
    // what keeps anonymous and framework caching working exactly as before.
    private static string Scoped(string key) =>
        Tenant.InFlight is { } tenant ? string.Concat(tenant.ToString("N"), ":", key) : key;

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        key = Scoped(key);
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
        key = Scoped(key);
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
        key = Scoped(key);
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
            await db.SaveChangesAsync(token).ConfigureAwait(false);
            return;
        }

        db.Set<CacheEntry>().Add(
            CacheEntry.For(key, value, expiresAt, now, absolute, sliding?.TotalSeconds));
        try
        {
            await db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateException error)
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
                // A key longer than the table's key column never stores on a provider that enforces the length
                // (PostgreSQL, SQL Server): the insert failed on truncation, and there is no row to update. Name
                // that, rather than surface a provider error about some value being too long for some column.
                // SQLite does not enforce the length, so it never reaches here for this reason.
                if (db.Model.FindEntityType(typeof(CacheEntry))?.FindProperty(nameof(CacheEntry.Key))?.GetMaxLength()
                        is { } maxLength
                    && key.Length > maxLength)
                {
                    throw new ArgumentException(
                        $"The cache key is {key.Length} characters long, and this database's cache table holds keys of "
                        + $"at most {maxLength}. Shorten the key — hash a long one, for example with SHA-256.",
                        nameof(key),
                        error);
                }

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
