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
            OpsHeader.Heading("System"),
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

        return OpsCard.Heading("Database").Class("mb-6")[
            OpsGrid[
                OpsStat
                    .Key("size")
                    .Value(db.SizeBytes is { } size ? DashboardParts.Bytes(size) : "—")
                    .Label("Size")
                    .Icon(OpsIconName.Database),
                OpsStat
                    .Key("journal")
                    .Value(db.JournalMode?.ToUpperInvariant() ?? "n/a")
                    .Label("Journal mode")
                    // WAL is the mode every Rask deployment expects; anything else is worth noticing.
                    .Tone(db.JournalMode is not null
                          && !db.JournalMode.Equals("wal", StringComparison.OrdinalIgnoreCase)
                        ? "warn"
                        : null)
                    .Icon(OpsIconName.Storage),
                OpsStat
                    .Key("fks")
                    .Value(db.ForeignKeys switch { true => "on", false => "off", null => "n/a" })
                    .Label("Foreign keys")
                    .Tone(db.ForeignKeys is false ? "warn" : null)
                    .Icon(OpsIconName.Gear),
                OpsStat
                    .Key("provider")
                    .Value(ShortProvider(db.Provider))
                    .Label("Provider")
                    .Icon(OpsIconName.Database)
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

        return OpsCard.Heading("Backup").Class("mb-6")[
            _replication is { } r
                ? Div.Class("mb-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    OpsStat
                        .Key("replication")
                        .Value(r.IsReplicating ? "running" : "stopped")
                        .Label("Continuous replication")
                        .Tone(r.IsReplicating ? null : "danger")
                        .Caption(r.LastStartedAt is { } started
                            ? $"since {DashboardParts.Ago(started.UtcDateTime, now)}"
                            : "never started")
                        .Icon(OpsIconName.Retry),
                    OpsStat
                        .Key("restarts")
                        .Value(r.RestartCount.ToString())
                        .Label("Restarts")
                        .Tone(r.RestartCount > 0 ? "warn" : null)
                        .Caption(r.LastError ?? "no failures recorded")
                        .Icon(OpsIconName.Warning)
                ]
                : null,
            // Restorability is its own row, and its own fact: "the replicator is running" above says
            // nothing about whether what it wrote can be read back.
            _verification is { } v
                ? Div.Class("mb-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    OpsStat
                        .Key("verification")
                        .Value(v.Level == BackupVerificationLevel.Verified
                            ? "restorable"
                            : v.Outcome.ToLowerInvariant())
                        .Label("Last verified restore")
                        // Broken is red; Unknown is amber. A check that races replication must not
                        // paint the tile red, or the tile stops being read.
                        .Tone(v.Level switch
                        {
                            BackupVerificationLevel.Verified => null,
                            BackupVerificationLevel.Broken => "danger",
                            _ => "warn",
                        })
                        .Caption(v.LastVerifiedAt is { } verified
                            ? $"verified {DashboardParts.Ago(verified.UtcDateTime, now)}"
                            : v.LastError ?? "never verified")
                        .Icon(v.Level == BackupVerificationLevel.Broken
                            ? OpsIconName.ShieldWarning
                            : OpsIconName.ShieldOk)
                ]
                : null,
            SnapshotList(now)
        ];
    }

    private Component SnapshotList(DateTime now) =>
        _snapshots.Count == 0
            ? Div.Class("text-xs text-ops-muted")["No snapshots stored."]
            : OpsTable[
                Thead.Class("border-b border-ops-line text-xs text-ops-muted")[
                    Tr[
                        Th.Class("px-3 py-2 font-medium")["Snapshot"],
                        Th.Class("px-3 py-2 font-medium")["Size"],
                        Th.Class("px-3 py-2 font-medium")["Taken"]
                    ]
                ],
                Tbody[_snapshots.Take(10).Select(s => Tr.Key(s.Name)
                    .Class("border-b border-ops-line/60 last:border-0")[
                    Td.Class($"px-3 py-2 {Ops.Mono}")[s.Name],
                    Td.Class("px-3 py-2 tabular-nums")[DashboardParts.Bytes(s.SizeBytes)],
                    Td.Class("px-3 py-2 text-xs text-ops-muted").Title(s.CreatedAt.ToString("u"))[
                        DashboardParts.Ago(s.CreatedAt, now)
                    ]
                ])]
            ];

    private Component? RecurringCard(DateTime now)
    {
        if (_recurring.Count == 0)
        {
            return null;
        }

        return OpsCard.Heading("Recurring jobs")[
            OpsTable[
                Thead.Class("border-b border-ops-line text-xs text-ops-muted")[
                    Tr[
                        Th.Class("px-3 py-2 font-medium")["Name"],
                        Th.Class("px-3 py-2 font-medium")["Every"],
                        Th.Class("px-3 py-2 font-medium")["Last enqueued"]
                    ]
                ],
                Tbody[_recurring.Select(r => Tr.Key(r.Name)
                    .Class("border-b border-ops-line/60 last:border-0")[
                    Td.Class($"px-3 py-2 {Ops.Mono}")[r.Name],
                    Td.Class("px-3 py-2")[DashboardParts.Duration(r.Interval)],
                    Td.Class("px-3 py-2")[
                        r.LastEnqueuedAt is { } last
                            ? Span.Class("text-xs text-ops-muted").Title(last.ToString("u"))[
                                DashboardParts.Ago(last, now)
                            ]
                            // Declared but never fired: either the app just started, or this one is stuck.
                            : OpsBadge.Label("never")
                    ]
                ])]
            ]
        ];
    }

    // "Microsoft.EntityFrameworkCore.Sqlite" reads better as "Sqlite" in a tile.
    private static string ShortProvider(string provider) =>
        provider.Split('.').LastOrDefault() is { Length: > 0 } last ? last : provider;
}
