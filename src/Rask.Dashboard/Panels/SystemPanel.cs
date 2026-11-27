using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;

namespace Rask.Dashboard.Panels;

/// <summary>Host-level facts: how the database is configured, what is scheduled, and whether backups run.</summary>
internal sealed class SystemPanel<TContext>(
    IDbContextFactory<TContext> contextFactory,
    IServiceProvider services) : ISystemPanelReader
    where TContext : DbContext
{
    private readonly JobsOptions? _jobOptions = services.GetService<JobsOptions>();
    private readonly IDashboardBackupProbe? _backup = services.GetService<IDashboardBackupProbe>();

    public bool HasBackupProbe => _backup is not null;

    public async Task<DatabaseInfo> Database(CancellationToken cancellationToken)
    {
        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
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

            long? size = long.TryParse(pageCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pages)
                          && long.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                ? pages * bytes
                : null;

            return new DatabaseInfo(provider, journalMode, string.Equals(foreignKeys, "1", StringComparison.Ordinal), size);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The registered recurring schedule joined to its durable state. Reads the schedule from
    /// <see cref="JobsOptions.RecurringJobs"/>, so it shows what the app declares even for a job that has
    /// never run yet — a table-only view would silently omit exactly the one that is failing to fire.
    /// </summary>
    public async Task<IReadOnlyList<RecurringJobRow>> RecurringJobs(CancellationToken cancellationToken)
    {
        if (_jobOptions is null || _jobOptions.RecurringJobs.Count == 0)
        {
            return [];
        }

        var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbScope = db.ConfigureAwait(false);
        if (db.Model.FindEntityType(typeof(RecurringJobState)) is null)
        {
            return [];
        }

        var names = _jobOptions.RecurringJobs.Select(r => r.Name).ToList();
        var state = await db.Set<RecurringJobState>()
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.LastEnqueuedAt, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        return [.. _jobOptions.RecurringJobs.Select(r =>
            new RecurringJobRow(r.Name, r.Schedule?.ToString() ?? "", state.GetValueOrDefault(r.Name)))];
    }

    public Task<BackupReplicationInfo?> Replication(CancellationToken cancellationToken) =>
        _backup?.Replication(cancellationToken) ?? Task.FromResult<BackupReplicationInfo?>(null);

    public Task<IReadOnlyList<BackupSnapshotInfo>> Snapshots(CancellationToken cancellationToken) =>
        _backup?.Snapshots(cancellationToken) ?? Task.FromResult<IReadOnlyList<BackupSnapshotInfo>>([]);

    public Task<BackupVerificationInfo?> Verification(CancellationToken cancellationToken) =>
        _backup?.Verification(cancellationToken) ?? Task.FromResult<BackupVerificationInfo?>(null);

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var commandScope = command.ConfigureAwait(false);
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value?.ToString();
    }
}
