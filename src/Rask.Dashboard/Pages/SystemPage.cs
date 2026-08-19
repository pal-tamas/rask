using Rask.Core.Routing;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The host: how the database is configured, what is scheduled to run, and whether backups are actually
/// happening. Everything here is read-only and cheap — it is the page you screenshot when someone asks
/// "is production set up correctly?".
/// </summary>
[Route("system")]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class SystemPage(
    ISystemPanelReader system,
    RaskDashboardOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private DatabaseInfo? _database;
    private IReadOnlyList<RecurringJobRow> _recurring = [];
    private BackupReplicationInfo? _replication;
    private BackupVerificationInfo? _verification;
    private IReadOnlyList<BackupSnapshotInfo> _snapshots = [];

    /// <inheritdoc />
    protected override RaskDashboardOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> LoadAsync(CancellationToken cancellationToken)
    {
        _database = await system.DatabaseAsync(cancellationToken).ConfigureAwait(false);
        _recurring = await system.RecurringJobsAsync(cancellationToken).ConfigureAwait(false);
        _replication = await system.ReplicationAsync(cancellationToken).ConfigureAwait(false);
        _verification = await system.VerificationAsync(cancellationToken).ConfigureAwait(false);
        _snapshots = await system.SnapshotsAsync(cancellationToken).ConfigureAwait(false);

        return string.Join('|',
            [$"{_database?.SizeBytes}:{_database?.JournalMode}:{_database?.ForeignKeys}",
             $"{_replication?.IsReplicating}:{_replication?.RestartCount}:{_replication?.LastError}",
             $"{_verification?.Outcome}:{_verification?.LastVerifiedAt?.Ticks ?? 0}:{_verification?.LastError}",
             $"{_snapshots.Count}:{_snapshots.FirstOrDefault()?.Name}",
             .. _recurring.Select(r => $"{r.Name}:{r.LastEnqueuedAt?.Ticks ?? 0}")]);
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (IsLoading)
        {
            return DashboardLoading;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        return [
            H1.Class("h4 mb-3")["System"],
            DashboardError.Message(LoadError),
            DatabaseCard(),
            BackupCard(now),
            RecurringCard(now),
            DashboardParked.Parked(IsParked).Resume(ResumeAsync),
        ];
    }

    private Component? DatabaseCard()
    {
        if (_database is not { } db)
        {
            return null;
        }

        return BsCard.Class("mb-4")[
            BsCardHeader["Database"],
            BsCardBody[
                BsRow.Class("g-3")[
                    BsCol.Sm(6).Lg(3)[BsStat
                        .Value(db.SizeBytes is { } size ? DashboardParts.Bytes(size) : "—")
                        .Label("Size")
                        .Icon(BsIconName.Database)],
                    BsCol.Sm(6).Lg(3)[BsStat
                        .Value(db.JournalMode?.ToUpperInvariant() ?? "n/a")
                        .Label("Journal mode")
                        .Tone(db.JournalMode is null ? null
                            : db.JournalMode.Equals("wal", StringComparison.OrdinalIgnoreCase)
                                ? BsColor.Success
                                : BsColor.Warning)
                        .Icon(BsIconName.HddStack)],
                    BsCol.Sm(6).Lg(3)[BsStat
                        .Value(db.ForeignKeys switch { true => "on", false => "off", null => "n/a" })
                        .Label("Foreign keys")
                        .Tone(db.ForeignKeys is false ? BsColor.Warning : null)
                        .Icon(BsIconName.Diagram3)],
                    BsCol.Sm(6).Lg(3)[BsStat
                        .Value(ShortProvider(db.Provider))
                        .Label("Provider")
                        .Icon(BsIconName.Database)]
                ]
            ]
        ];
    }

    private Component? BackupCard(DateTime now)
    {
        // No probe registered means the app didn't say how it backs up — showing "no backups" would be a
        // claim the dashboard can't support, so the card stays away entirely.
        if (!system.HasBackupProbe)
        {
            return null;
        }

        return BsCard.Class("mb-4")[
            BsCardHeader["Backup"],
            BsCardBody[
                _replication is { } r
                    ? BsRow.Class("g-3 mb-3")[
                        BsCol.Sm(6).Lg(4)[BsStat
                            .Value(r.IsReplicating ? "running" : "stopped")
                            .Label("Continuous replication")
                            .Tone(r.IsReplicating ? BsColor.Success : BsColor.Danger)
                            .Caption(r.LastStartedAt is { } started
                                ? $"since {DashboardParts.Ago(started.UtcDateTime, now)}"
                                : "never started")
                            .Icon(BsIconName.ArrowRepeat)],
                        BsCol.Sm(6).Lg(4)[BsStat
                            .Value(r.RestartCount.ToString())
                            .Label("Restarts")
                            .Tone(r.RestartCount > 0 ? BsColor.Warning : null)
                            .Caption(r.LastError ?? "no failures recorded")
                            .Icon(BsIconName.ExclamationTriangle)]
                    ]
                    : null,
                // Restorability is its own row, and its own fact: "the replicator is running" above says
                // nothing about whether what it wrote can be read back.
                _verification is { } v
                    ? BsRow.Class("g-3 mb-3")[
                        BsCol.Sm(6).Lg(4)[BsStat
                            .Value(v.Level == BackupVerificationLevel.Verified ? "restorable" : v.Outcome.ToLowerInvariant())
                            .Label("Last verified restore")
                            .Tone(v.Level switch
                            {
                                BackupVerificationLevel.Verified => BsColor.Success,
                                BackupVerificationLevel.Broken => BsColor.Danger,
                                _ => BsColor.Warning,
                            })
                            .Caption(v.LastVerifiedAt is { } verified
                                ? $"verified {DashboardParts.Ago(verified.UtcDateTime, now)}"
                                : v.LastError ?? "never verified")
                            .Icon(v.Level == BackupVerificationLevel.Broken
                                ? BsIconName.ShieldExclamation
                                : BsIconName.ShieldCheck)]
                    ]
                    : null,
                SnapshotList(now)
            ]
        ];
    }

    private Component SnapshotList(DateTime now) =>
        _snapshots.Count == 0
            ? Div.Class("text-body-secondary small")["No snapshots stored."]
            : BsTable.Small(true).Responsive(true).Class("mb-0")[
                Thead[Tr[Th["Snapshot"], Th["Size"], Th["Taken"]]],
                Tbody[_snapshots.Take(10).Select(s => Tr.Key(s.Name)[
                    Td.Class("font-monospace small")[s.Name],
                    Td[DashboardParts.Bytes(s.SizeBytes)],
                    Td.Title(s.CreatedAt.ToString("u"))[DashboardParts.Ago(s.CreatedAt, now)]
                ])]
            ];

    private Component? RecurringCard(DateTime now)
    {
        if (_recurring.Count == 0)
        {
            return null;
        }

        return BsCard[
            BsCardHeader["Recurring jobs"],
            BsCardBody[
                BsTable.Small(true).Responsive(true).Class("mb-0")[
                    Thead[Tr[Th["Name"], Th["Every"], Th["Last enqueued"]]],
                    Tbody[_recurring.Select(r => Tr.Key(r.Name)[
                        Td.Class("font-monospace small")[r.Name],
                        Td[DashboardParts.Duration(r.Interval)],
                        Td[r.LastEnqueuedAt is { } last
                            ? Span.Title(last.ToString("u"))[DashboardParts.Ago(last, now)]
                            // Declared but never fired: either the app just started, or this one is stuck.
                            : BsBadge.Color(BsColor.Secondary)["never"]]
                    ])]
                ]
            ]
        ];
    }

    // "Microsoft.EntityFrameworkCore.Sqlite" reads better as "Sqlite" in a tile.
    private static string ShortProvider(string provider) =>
        provider.Split('.').LastOrDefault() is { Length: > 0 } last ? last : provider;
}
