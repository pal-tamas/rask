using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Storage;

namespace Rask.Dashboard.Panels;

/// <summary>
/// The stored-file reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface IStoragePanelReader
{
    /// <summary><c>false</c> when Rask.Storage isn't registered or its table isn't mapped.</summary>
    bool IsAvailable { get; }

    /// <summary>File count, stored bytes, how many are public, usage per provider, and the sweep's settings.</summary>
    Task<StorageStats> StatsAsync(CancellationToken cancellationToken);

    /// <summary>One page of files, newest first, optionally filtered by a substring of the name.</summary>
    Task<(IReadOnlyList<StoredFileRow> Rows, int Total)> PageAsync(
        string? search, int skip, int take, CancellationToken cancellationToken);
}

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

    public async Task<StorageStats> StatsAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return StorageStats.Empty;
        }

        var options = services.GetRequiredService<StorageOptions>();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task<(IReadOnlyList<StoredFileRow> Rows, int Total)> PageAsync(
        string? search, int skip, int take, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return ([], 0);
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
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

/// <summary>Storage totals for the Storage tab.</summary>
/// <param name="Files">Rows in the stored-file table.</param>
/// <param name="Bytes">Total size of the stored files.</param>
/// <param name="Public">How many are served to anyone with the link.</param>
/// <param name="ByProvider">Files and bytes per provider, largest first.</param>
/// <param name="ActiveProvider">The provider new files are written to.</param>
/// <param name="SweepInterval">How often the orphan sweep runs.</param>
/// <param name="OrphanGracePeriod">How old unreferenced bytes must be before the sweep removes them.</param>
public readonly record struct StorageStats(
    int Files,
    long Bytes,
    int Public,
    IReadOnlyList<ProviderUsage> ByProvider,
    StorageProvider ActiveProvider,
    TimeSpan SweepInterval,
    TimeSpan OrphanGracePeriod)
{
    /// <summary>No files, and nothing known about the configuration.</summary>
    public static StorageStats Empty { get; } = new(0, 0, 0, [], StorageProvider.Disk, TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>Files and bytes kept by one provider.</summary>
public sealed record ProviderUsage(StorageProvider Provider, int Files, long Bytes);

/// <summary>One stored file, as the console lists it.</summary>
/// <param name="Id">The file's id.</param>
/// <param name="Name">Its display name — text an uploader chose.</param>
/// <param name="ContentType">The sniffed media type.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Provider">Where its bytes are.</param>
/// <param name="Public">Whether anyone with the link may fetch it.</param>
/// <param name="CreatedAt">When it was saved (UTC).</param>
public sealed record StoredFileRow(
    Guid Id, string Name, string ContentType, long Size, StorageProvider Provider, bool Public, DateTime CreatedAt);
