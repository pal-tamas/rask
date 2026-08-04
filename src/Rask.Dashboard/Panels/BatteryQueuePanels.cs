using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cache;
using Rask.Jobs;
using Rask.Mail;
using Rask.Outbox;

namespace Rask.Dashboard.Panels;

/// <summary>Background jobs — <see cref="Job"/>.</summary>
internal sealed class JobsQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, Job>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly JobOptions? _options = services.GetService<JobOptions>();

    public override string Slug => "jobs";

    public override string Title => "Jobs";

    public override BsIconName Icon => BsIconName.GearWideConnected;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    protected override string RunAtProperty => nameof(Job.RunAt);

    protected override Expression<Func<Job, QueueRow>> Projection => j => new QueueRow(
        j.Id, j.Type, j.Payload, j.CreatedAt, j.RunAt, j.ProcessedAt, j.Attempts, j.Error, j.Payload);
}

/// <summary>Domain events awaiting publication — <see cref="OutboxMessage"/>.</summary>
internal sealed class OutboxQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, OutboxMessage>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly OutboxOptions? _options = services.GetService<OutboxOptions>();

    public override string Slug => "outbox";

    public override string Title => "Outbox";

    public override BsIconName Icon => BsIconName.BoxArrowUpRight;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    // The outbox has no scheduled-run column: an event is eligible the moment it is written, and a retry
    // is immediate rather than backed off. OccurredAt stands in for both so the shared projection holds —
    // which correctly leaves the Delayed count permanently zero for this queue.
    protected override string RunAtProperty => nameof(OutboxMessage.OccurredAt);

    protected override Expression<Func<OutboxMessage, QueueRow>> Projection => m => new QueueRow(
        m.Id, m.Type, m.Payload, m.OccurredAt, m.OccurredAt, m.ProcessedAt, m.Attempts, m.Error, m.Payload);
}

/// <summary>Queued email — <see cref="QueuedMail"/>.</summary>
internal sealed class MailQueuePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    TimeProvider timeProvider,
    IServiceProvider services)
    : QueuePanelBase<TContext, QueuedMail>(contextFactory, timeProvider)
    where TContext : DbContext
{
    private readonly MailOptions? _options = services.GetService<MailOptions>();

    public override string Slug => "mail";

    public override string Title => "Mail";

    public override BsIconName Icon => BsIconName.Envelope;

    public override int MaxAttempts => _options?.MaxAttempts ?? 0;

    protected override bool IsRegistered => _options is not null;

    // For mail the useful "type" is the subject and the useful summary is who it went to — the two things
    // you scan a mail log for. Recipients are stored as a JSON array; the detail page deserializes it
    // properly, the list just shows the raw string.
    protected override string RunAtProperty => nameof(QueuedMail.RunAt);

    protected override Expression<Func<QueuedMail, QueueRow>> Projection => m => new QueueRow(
        m.Id, m.Subject, m.To, m.CreatedAt, m.RunAt, m.ProcessedAt, m.Attempts, m.Error, m.To);
}

/// <summary>
/// The cache reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface ICachePanelReader
{
    /// <summary><c>false</c> when Rask.Cache isn't registered or its table isn't mapped.</summary>
    bool IsAvailable { get; }

    /// <summary>Entry count, total stored bytes, and how many are expired but not yet swept.</summary>
    Task<CacheStats> StatsAsync(CancellationToken cancellationToken);

    /// <summary>One page of keys, soonest to expire first.</summary>
    Task<(IReadOnlyList<CacheKeyRow> Rows, int Total)> PageAsync(
        string? search, int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Drops one key. Safe by nature — a cache miss is a recompute, not a lost fact — which is why this
    /// sits in the Safe action tier while flushing everything does not.
    /// </summary>
    Task<int> EvictAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every entry. Correctness-safe for the same reason, but a cold cache on a busy app means a
    /// stampede of recomputes, so it needs <see cref="RaskDashboardActions.Destructive"/>.
    /// </summary>
    Task<int> FlushAsync(CancellationToken cancellationToken);
}

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

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

/// <summary>Cache totals for the overview tiles.</summary>
/// <param name="Entries">Rows in the cache table.</param>
/// <param name="Bytes">Total stored value bytes.</param>
/// <param name="Expired">Rows past their expiry that the purge sweep hasn't removed yet.</param>
public readonly record struct CacheStats(int Entries, long Bytes, int Expired);

/// <summary>One cache entry, without its value.</summary>
/// <param name="Key">The cache key.</param>
/// <param name="Bytes">Size of the stored value.</param>
/// <param name="CreatedAt">When it was written (UTC).</param>
/// <param name="ExpiresAt">When it stops being served (UTC).</param>
/// <param name="SlidingSeconds">The sliding window, if it has one.</param>
public sealed record CacheKeyRow(string Key, int Bytes, DateTime CreatedAt, DateTime ExpiresAt, double? SlidingSeconds);
