using Rask.Dashboard.Panels;
using Rask.SQLite.Litestream;
using Rask.SQLite.Snapshots;

namespace Rask.Example.Shop.Features.Ops;

/// <summary>
/// Lights up the dashboard's Backup card.
/// </summary>
/// <remarks>
/// <para>
/// <c>Rask.Dashboard</c> deliberately doesn't reference <c>Rask.SQLite.Litestream</c> or
/// <c>Rask.SQLite.Snapshots</c>: those pull a native SQLitePCLRaw provider bundle, which would force a
/// provider choice on every consumer and tie a provider-agnostic dashboard to SQLite. So the app — which
/// already references both, because it uses them — supplies the reading instead.
/// </para>
/// <para>
/// <b>Both dependencies are optional, and that is load-bearing.</b> <c>AddRaskSqliteLitestream</c> is
/// config-gated in this app (and in everything <c>rask new</c> scaffolds): with no
/// <c>Litestream:ReplicaUrl</c> configured it never runs, so <see cref="LitestreamStatus"/> is not in the
/// container. Taking it as a required dependency makes the app start cleanly and then throw the first
/// time somebody opens the System panel — a failure that only appears in the environment that skipped the
/// configuration, which is usually the one you didn't test.
/// </para>
/// </remarks>
public sealed class BackupProbe(LitestreamStatus? litestream = null, ISqliteSnapshotStore? snapshots = null)
    : IDashboardBackupProbe
{
    /// <inheritdoc/>
    public Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken)
    {
        // null, not a fabricated "stopped" reading: continuous backup isn't configured here, and saying
        // it is stopped would report a problem that doesn't exist.
        if (litestream is null)
        {
            return Task.FromResult<BackupReplicationInfo?>(null);
        }

        var status = litestream.Current;
        return Task.FromResult<BackupReplicationInfo?>(new BackupReplicationInfo(
            status.IsReplicating,
            status.LastStartedAt,
            status.RestartCount,
            status.LastError));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken)
    {
        if (snapshots is null)
        {
            return [];
        }

        return
        [
            .. (await snapshots.ListAsync(cancellationToken).ConfigureAwait(false))
                .Select(s => new BackupSnapshotInfo(s.Name, s.SizeBytes, s.CreatedAt)),
        ];
    }
}
