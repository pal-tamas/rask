using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Storage.Backends;

namespace Rask.Storage;

/// <summary>What one sweep did.</summary>
internal readonly record struct SweepResult(int Deleted, int Orphans, bool Tripped);

/// <summary>Rows whose bytes are not on disk: how many, and the first few ids.</summary>
internal sealed record MissingFiles(int Count, IReadOnlyList<Guid> Examples);

/// <summary>When the sweep is allowed to delete at all.</summary>
internal static class SweepPolicy
{
    /// <summary>
    /// A disk root belongs to the app. A bucket is easily shared by two environments, and without a prefix a
    /// key's shape alone cannot tell this app's orphans from another app's files — so there it only reports.
    /// </summary>
    internal static bool MayDelete(StorageProvider provider, string prefix) =>
        provider == StorageProvider.Disk || prefix.Length > 0;
}

/// <summary>
/// Removes bytes that have no <see cref="StoredFile"/> row once they are older than
/// <see cref="StorageOptions.OrphanGracePeriod"/>: what a save that failed between writing the bytes and the
/// row, or a delete that could not reach the store, leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>It fails closed.</b> Any error while asking the database which ids exist ends the run before anything is
/// deleted — a query that failed must never read as "no rows". Candidates are checked again immediately before
/// they are deleted, so a row written while the listing ran keeps its bytes.
/// </para>
/// <para>
/// <b>It deletes nothing from a store it cannot be sure is its own.</b> Not against an empty table — an app
/// pointed at a fresh database sees every object as an orphan. Not when more than a tenth of what it looked at
/// (and more than 100 objects) would go. And not at all on S3 or Azure without a
/// <see cref="StorageOptions.Prefix"/>, where it only reports what it found (<see cref="SweepPolicy"/>).
/// </para>
/// <para>
/// It never writes to the database, and deleting is idempotent, so several instances sweeping one store is
/// safe with no lease.
/// </para>
/// <para>
/// <b>On disk it also looks the other way</b>: rows whose bytes are gone. That is what a database restored without its
/// files looks like, and it would otherwise surface only as 404s (#1077). It is reported, never repaired — the row is
/// the app's, and the fix is to restore the files.
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
    internal const int MissingExamples = 5;

    /// <summary>How long after start the first sweep waits, before its jitter.</summary>
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Once shortly after start rather than a whole interval later: an app redeployed more often than the
            // interval would otherwise never sweep at all. Jittered, so a fleet restarting together does not list
            // the same bucket in the same second.
            var jitter = TimeSpan.FromSeconds(Random.Shared.Next(0, 120));
            await Task.Delay(InitialDelay + jitter, runtime.Time, stoppingToken).ConfigureAwait(false);
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            await ReportMissingOnceAsync(stoppingToken).ConfigureAwait(false);

            using var timer = new PeriodicTimer(runtime.Options.SweepInterval, runtime.Time);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await ReportMissingOnceAsync(stoppingToken).ConfigureAwait(false);
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

    private async Task ReportMissingOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FindMissingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A transient database error must not fault the host; the next run retries.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "The storage check for files missing from disk failed; retrying on the next interval.");
        }
    }

    /// <summary>
    /// Count disk rows older than the grace period whose bytes are not under the root, and warn when there are any.
    /// Null when the store is not a disk.
    /// </summary>
    /// <remarks>
    /// Rows newer than the grace period are skipped, so a delete that has removed the bytes and not yet the row is
    /// never reported.
    /// </remarks>
    internal async Task<MissingFiles?> FindMissingAsync(CancellationToken cancellationToken)
    {
        if (runtime.Backend is not DiskBlobBackend disk)
        {
            return null;
        }

        var cutoff = (runtime.Time.GetUtcNow() - runtime.Options.OrphanGracePeriod).UtcDateTime;
        var missing = 0;
        var examples = new List<Guid>();

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var rows = db.Set<StoredFile>()
                .AsNoTracking()
                .Where(f => f.Provider == StorageProvider.Disk && f.CreatedAt < cutoff)
                .Select(f => new { f.Id, f.Key })
                .AsAsyncEnumerable();

            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (KeyLayout.IsValid(row.Key) && disk.Exists(row.Key))
                {
                    continue;
                }

                missing++;
                if (examples.Count < MissingExamples)
                {
                    examples.Add(row.Id);
                }
            }
        }

        if (missing > 0)
        {
            logger.LogWarning(
                "{Missing} stored files have a StoredFile row but no bytes under {Root} (for example {Examples}), and answer "
                + "404. A database restored without its files looks like this: restore the .files.tgz archive that "
                + "rask db backup wrote beside the database.",
                missing, runtime.Options.Disk.Root, string.Join(", ", examples));
        }

        return new MissingFiles(missing, examples);
    }

    internal async Task<SweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        var options = runtime.Options;
        var backend = runtime.Backend;
        var cutoff = runtime.Time.GetUtcNow() - options.OrphanGracePeriod;

        // First, whatever the listing below decides: a crashed save's spool file is this process's own.
        await backend.DeleteStaleSpoolAsync(cutoff, cancellationToken).ConfigureAwait(false);

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

        if (orphanCount == 0)
        {
            return new SweepResult(0, 0, Tripped: false);
        }

        if (!SweepPolicy.MayDelete(backend.Provider, options.Prefix))
        {
            logger.LogWarning(
                "The storage orphan sweep found {Orphans} objects in {Provider} with no StoredFile row and deleted none: "
                + "with no Rask__Storage__Prefix it cannot tell this app's orphans from another environment's files in the "
                + "same bucket. Set Rask__Storage__Prefix to let it clean up.",
                orphanCount, backend.Provider);
            return new SweepResult(0, orphanCount, Tripped: true);
        }

        if (!await AnyRowAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogError(
                "The storage orphan sweep found {Orphans} stored objects but the StoredFile table is empty, and deleted "
                + "none of them: an empty table usually means the app is reading the wrong or a freshly created "
                + "database. Check ConnectionStrings:App.",
                orphanCount);
            return new SweepResult(0, orphanCount, Tripped: true);
        }

        if (orphanCount > Math.Max(BreakerFloor, eligible / 10))
        {
            logger.LogError(
                "The storage orphan sweep found {Orphans} of {Eligible} stored objects with no StoredFile row and deleted "
                + "none of them: that many usually means the app is reading the wrong database, or two environments share "
                + "one bucket and prefix. Check ConnectionStrings:App and Rask__Storage__Prefix.",
                orphanCount, eligible);
            return new SweepResult(0, orphanCount, Tripped: true);
        }

        var deleted = 0;
        foreach (var chunk in orphans.Chunk(BatchSize))
        {
            // Checked again just before deleting: a row written while the listing ran keeps its bytes.
            var present = await ExistingIdsAsync([.. chunk.Select(o => o.Id)], cancellationToken).ConfigureAwait(false);
            foreach (var (id, key) in chunk)
            {
                if (present.Contains(id))
                {
                    continue;
                }

                await backend.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation("The storage orphan sweep removed {Deleted} objects with no StoredFile row.", deleted);
        }

        return new SweepResult(deleted, orphanCount, Tripped: false);
    }

    private async Task<int> CollectOrphansAsync(List<(Guid Id, string Key)> batch, List<(Guid Id, string Key)> orphans,
        CancellationToken cancellationToken)
    {
        var existing = await ExistingIdsAsync(batch.ConvertAll(b => b.Id), cancellationToken).ConfigureAwait(false);

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

    private async Task<HashSet<Guid>> ExistingIdsAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return (await db.Set<StoredFile>()
                .Where(f => ids.Contains(f.Id))
                .Select(f => f.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet();
        }
    }

    private async Task<bool> AnyRowAsync(CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            return await db.Set<StoredFile>().AnyAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
