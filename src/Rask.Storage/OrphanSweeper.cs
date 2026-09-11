using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Storage;

/// <summary>What one sweep did.</summary>
internal readonly record struct SweepResult(int Deleted, int Orphans, bool Tripped);

/// <summary>
/// Removes bytes that have no <see cref="StoredFile"/> row once they are older than
/// <see cref="StorageOptions.OrphanGracePeriod"/>: what a save that failed between writing the bytes and the
/// row, or a delete that could not reach the store, leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>It fails closed.</b> Any error while asking the database which ids exist ends the run before anything is
/// deleted — a query that failed must never read as "no rows". Each candidate is checked again immediately
/// before it is deleted, so a row written while the listing ran keeps its bytes.
/// </para>
/// <para>
/// <b>It refuses to delete a lot.</b> If more than a tenth of what it looked at (and more than 100 objects)
/// would go, it deletes nothing and logs an error. That is the signature of an app pointed at the wrong or an
/// empty database, or of two environments sharing one bucket and prefix — cases where "no row" means "wrong
/// place", not "orphan".
/// </para>
/// <para>
/// It never writes to the database, and deleting is idempotent, so several instances sweeping one store is
/// safe with no lease.
/// </para>
/// </remarks>
internal sealed class OrphanSweeper<TContext>(
    IDbContextFactory<TContext> contextFactory,
    StorageRuntime runtime,
    ILogger<OrphanSweeper<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    internal const int BatchSize = 500;
    internal const int MaxDeletesPerRun = 10_000;
    internal const int BreakerFloor = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(runtime.Options.SweepInterval, runtime.Time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transient store or database error must not fault the host; the next run retries.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "The storage orphan sweep failed and stopped; retrying on the next interval.");
        }
    }

    internal async Task<SweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var options = runtime.Options;
        var backend = runtime.Backend;
        var cutoff = runtime.Time.GetUtcNow() - options.OrphanGracePeriod;

        var eligible = 0;
        var orphanCount = 0;
        var orphans = new List<(Guid Id, string Key)>();
        var batch = new List<(Guid Id, string Key)>(BatchSize);

        await foreach (var entry in backend.ListAsync(options.Prefix, cancellationToken).ConfigureAwait(false))
        {
            if (entry.LastModified > cutoff || !KeyLayout.TryParse(entry.Key, options.Prefix, out var id))
            {
                continue;
            }

            eligible++;
            batch.Add((id, entry.Key));
            if (batch.Count == BatchSize)
            {
                orphanCount += await CollectOrphansAsync(batch, orphans, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            orphanCount += await CollectOrphansAsync(batch, orphans, cancellationToken).ConfigureAwait(false);
        }

        if (orphanCount > Math.Max(BreakerFloor, eligible / 10))
        {
            logger.LogError(
                "The storage orphan sweep found {Orphans} of {Eligible} stored objects with no StoredFile row and deleted "
                + "none of them: that many usually means the app is reading the wrong database, or two environments share "
                + "one bucket and prefix. Check ConnectionStrings:App and Storage__Prefix.",
                orphanCount, eligible);
            return new SweepResult(0, orphanCount, Tripped: true);
        }

        var deleted = 0;
        foreach (var (id, key) in orphans)
        {
            if (await RowExistsAsync(id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await backend.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            deleted++;
        }

        await backend.DeleteStaleSpoolAsync(cutoff, cancellationToken).ConfigureAwait(false);

        if (deleted > 0)
        {
            logger.LogInformation("The storage orphan sweep removed {Deleted} objects with no StoredFile row.", deleted);
        }

        return new SweepResult(deleted, orphanCount, Tripped: false);
    }

    private async Task<int> CollectOrphansAsync(List<(Guid Id, string Key)> batch, List<(Guid Id, string Key)> orphans,
        CancellationToken cancellationToken)
    {
        var ids = batch.ConvertAll(b => b.Id);
        HashSet<Guid> existing;
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            existing = (await db.Set<StoredFile>()
                .Where(f => ids.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet();
        }

        var found = 0;
        foreach (var candidate in batch)
        {
            if (existing.Contains(candidate.Id))
            {
                continue;
            }

            found++;
            if (orphans.Count < MaxDeletesPerRun)
            {
                orphans.Add(candidate);
            }
        }

        return found;
    }

    private async Task<bool> RowExistsAsync(Guid id, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Set<StoredFile>().AnyAsync(f => f.Id == id, cancellationToken).ConfigureAwait(false);
        }
    }
}
