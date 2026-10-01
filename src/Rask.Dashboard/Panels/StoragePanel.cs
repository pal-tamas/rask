using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Storage;

namespace Rask.Dashboard.Panels;

/// <summary>
/// Stored files, read straight from the <see cref="StoredFile"/> table. Read-only by design: deleting a file from
/// the console would leave whatever entity still points at it with a broken link.
/// </summary>
internal sealed class StoragePanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceProvider services) : IStoragePanelReader
    where TContext : DbContext
{
    private bool? _available;

    public bool IsAvailable => _available ??= IsRegistered() && IsMapped();

    public async Task<StorageStats> Stats(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return StorageStats.Empty;
        }

        var options = services.GetRequiredService<StorageOptions>();
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var files = db.Set<StoredFile>();

        var byProvider = await files
            .GroupBy(f => f.Provider)
            .Select(g => new ProviderUsage(g.Key, g.Count(), g.Sum(f => f.Size)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new StorageStats(
            Files: byProvider.Sum(p => p.Files),
            Bytes: byProvider.Sum(p => p.Bytes),
            Public: await files.CountAsync(f => f.Public, cancellationToken).ConfigureAwait(false),
            ByProvider: [.. byProvider.OrderByDescending(p => p.Files)],
            ActiveProvider: options.Provider,
            SweepInterval: options.SweepInterval,
            OrphanGracePeriod: options.OrphanGracePeriod);
    }

    public async Task<(IReadOnlyList<StoredFileRow> Rows, int Total)> Page(
        string? search, int skip, int take, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ([], 0);
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        var query = db.Set<StoredFile>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f => f.Name.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(f => new StoredFileRow(f.Id, f.Name, f.ContentType, f.Size, f.Provider, f.Public, f.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (rows, total);
    }

    // Asked of the container rather than by resolving IFiles: resolving it would build and validate the storage
    // options, and a panel has no business being the thing that throws about configuration.
    private bool IsRegistered() =>
        services.GetService<IServiceProviderIsService>()?.IsService(typeof(IFiles)) == true;

    private bool IsMapped()
    {
        using var db = contextFactory.CreateDbContext();
        return db.Model.FindEntityType(typeof(StoredFile)) is not null;
    }
}
