using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Jobs;

namespace Rask.Dashboard.Panels;

/// <summary>
/// Backup state for the system panel. The dashboard deliberately takes no dependency on
/// <c>Rask.SQLite.Litestream</c> or <c>Rask.SQLite.Snapshots</c>: those pull a native SQLitePCLRaw provider
/// bundle, and the dashboard itself is provider-agnostic — it reads EF entities and works just as well on
/// Postgres. Register an implementation to light up the backup tiles; without one they stay hidden.
/// <para>
/// The data it needs is public API: <c>LitestreamStatus.Current</c> and
/// <c>ISqliteSnapshotStore.ListAsync(ct)</c>.
/// </para>
/// </summary>
public interface IDashboardBackupProbe
{
    /// <summary>Continuous-replication state, or <c>null</c> if the app doesn't run any.</summary>
    Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken);

    /// <summary>Stored snapshots, newest first. Empty when the app takes none.</summary>
    Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken);
}

/// <summary>Continuous-backup liveness, as the dashboard displays it.</summary>
/// <param name="IsReplicating">Whether replication is running right now.</param>
/// <param name="LastStartedAt">When the current or most recent run started.</param>
/// <param name="RestartCount">How many times it has restarted — climbing means flapping.</param>
/// <param name="LastError">The most recent failure, if any.</param>
public sealed record BackupReplicationInfo(
    bool IsReplicating, DateTimeOffset? LastStartedAt, int RestartCount, string? LastError);

/// <summary>One stored snapshot.</summary>
/// <param name="Name">The snapshot's name.</param>
/// <param name="SizeBytes">Its size on disk.</param>
/// <param name="CreatedAt">When it was taken (UTC).</param>
public sealed record BackupSnapshotInfo(string Name, long SizeBytes, DateTime CreatedAt);

/// <summary>A recurring job's schedule joined to when it actually last fired.</summary>
/// <param name="Name">The durable name.</param>
/// <param name="Interval">How often it should run.</param>
/// <param name="LastEnqueuedAt">When it was last enqueued, or <c>null</c> if it never has been.</param>
public sealed record RecurringJobRow(string Name, TimeSpan Interval, DateTime? LastEnqueuedAt);

/// <summary>How the database is configured, as far as the dashboard can see from the open connection.</summary>
/// <param name="Provider">The EF provider name.</param>
/// <param name="JournalMode">SQLite <c>journal_mode</c>, or <c>null</c> on another provider.</param>
/// <param name="ForeignKeys">SQLite <c>foreign_keys</c>, or <c>null</c> on another provider.</param>
/// <param name="SizeBytes">Database size in bytes, or <c>null</c> when the provider can't report it cheaply.</param>
public sealed record DatabaseInfo(string Provider, string? JournalMode, bool? ForeignKeys, long? SizeBytes);

/// <summary>
/// The system reader, without the context type parameter — pages aren't generic, so they resolve this.
/// </summary>
public interface ISystemPanelReader
{
    /// <summary>Whether an <see cref="IDashboardBackupProbe"/> is registered, so the backup card can hide.</summary>
    bool HasBackupProbe { get; }

    /// <summary>Provider, SQLite pragmas where applicable, and database size.</summary>
    Task<DatabaseInfo> DatabaseAsync(CancellationToken cancellationToken);

    /// <summary>The declared recurring schedule joined to when each job last fired.</summary>
    Task<IReadOnlyList<RecurringJobRow>> RecurringJobsAsync(CancellationToken cancellationToken);

    /// <summary>Continuous-replication state, or <c>null</c>.</summary>
    Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken);

    /// <summary>Stored snapshots, newest first.</summary>
    Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken);
}

/// <summary>Host-level facts: how the database is configured, what is scheduled, and whether backups run.</summary>
internal sealed class SystemPanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceProvider services) : ISystemPanelReader
    where TContext : DbContext
{
    private readonly JobOptions? _jobOptions = services.GetService<JobOptions>();
    private readonly IDashboardBackupProbe? _backup = services.GetService<IDashboardBackupProbe>();

    public bool HasBackupProbe => _backup is not null;

    public async Task<DatabaseInfo> DatabaseAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var provider = db.Database.ProviderName ?? "unknown";

        // Read through the raw DbConnection rather than a SQLite package: PRAGMA is just SQL, so this needs
        // no provider reference and simply reports nothing on a provider that doesn't understand it.
        if (!provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseInfo(provider, null, null, null);
        }

        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var journalMode = await ScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false);
            var foreignKeys = await ScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false);
            var pageCount = await ScalarAsync(connection, "PRAGMA page_count;", cancellationToken).ConfigureAwait(false);
            var pageSize = await ScalarAsync(connection, "PRAGMA page_size;", cancellationToken).ConfigureAwait(false);

            long? size = long.TryParse(pageCount, out var pages) && long.TryParse(pageSize, out var bytes)
                ? pages * bytes
                : null;

            return new DatabaseInfo(provider, journalMode, foreignKeys == "1", size);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The registered recurring schedule joined to its durable state. Reads the schedule from
    /// <see cref="JobOptions.RecurringJobs"/>, so it shows what the app declares even for a job that has
    /// never run yet — a table-only view would silently omit exactly the one that is failing to fire.
    /// </summary>
    public async Task<IReadOnlyList<RecurringJobRow>> RecurringJobsAsync(CancellationToken cancellationToken)
    {
        if (_jobOptions is null || _jobOptions.RecurringJobs.Count == 0)
        {
            return [];
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        if (db.Model.FindEntityType(typeof(RecurringJobState)) is null)
        {
            return [];
        }

        var names = _jobOptions.RecurringJobs.Select(r => r.Name).ToList();
        var state = await db.Set<RecurringJobState>()
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.LastEnqueuedAt, cancellationToken)
            .ConfigureAwait(false);

        return [.. _jobOptions.RecurringJobs.Select(r =>
            new RecurringJobRow(r.Name, r.Interval, state.GetValueOrDefault(r.Name)))];
    }

    public Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken) =>
        _backup?.ReplicationAsync(cancellationToken) ?? Task.FromResult<BackupReplicationInfo?>(null);

    public Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken) =>
        _backup?.SnapshotsAsync(cancellationToken) ?? Task.FromResult<IReadOnlyList<BackupSnapshotInfo>>([]);

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value?.ToString();
    }
}
