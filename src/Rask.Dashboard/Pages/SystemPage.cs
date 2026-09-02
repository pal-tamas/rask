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

        // A leader list rather than four tiles. These are four short scalars an operator reads once to
        // confirm the deployment is configured the way they think — a headline number's worth of weight
        // each was three times the space and none of the extra meaning.
        return OpsCard.Heading("Database").Class("mb-4 sm:mb-6")[
            UiDetailList[
                UiDetailRow
                    .Key("size")
                    .Label("Size")
                    .Value(db.SizeBytes is { } size ? DashboardParts.Bytes(size) : "—")
                    .Mono(true),
                UiDetailRow
                    .Key("journal")
                    .Label("Journal mode")
                    .Value(db.JournalMode?.ToUpperInvariant() ?? "n/a")
                    .Mono(true)
                    // WAL is the mode every Rask deployment expects; anything else is worth noticing.
                    .Tone(db.JournalMode is not null
                          && !db.JournalMode.Equals("wal", StringComparison.OrdinalIgnoreCase)
                        ? UiTone.Warn
                        : null),
                UiDetailRow
                    .Key("fks")
                    .Label("Foreign keys")
                    .Value(db.ForeignKeys switch { true => "on", false => "off", null => "n/a" })
                    .Mono(true)
                    .Tone(db.ForeignKeys is false ? UiTone.Warn : null),
                UiDetailRow
                    .Key("provider")
                    .Label("Provider")
                    .Value(ShortProvider(db.Provider))
                    .Mono(true)
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
                        .Icon(UiIconName.Retry),
                    OpsStat
                        .Key("restarts")
                        .Value(r.RestartCount.ToString())
                        .Label("Restarts")
                        .Tone(r.RestartCount > 0 ? "warn" : null)
                        .Caption(r.LastError ?? "no failures recorded")
                        .Icon(UiIconName.Warning)
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
                            ? UiIconName.ShieldWarning
                            : UiIconName.ShieldOk)
                ]
                : null,
            SnapshotList(now)
        ];
    }

    private Component SnapshotList(DateTime now) =>
        _snapshots.Count == 0
            ? Div.Class("text-xs text-ui-muted")["No snapshots stored."]
            : OpsTable[
                Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                    Tr[
                        Th.Class("px-3 py-2 font-medium")["Snapshot"],
                        Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Size"],
                        Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Taken"]
                    ]
                ],
                Tbody[_snapshots.Take(10).Select(s => Tr.Key(s.Name)
                    .Class("border-b border-ui-line/60 last:border-0")[
                    Td.Class($"w-full max-w-0 px-3 py-2 align-top {Ops.Mono}")[
                        Div.Class("break-all")[s.Name],
                        Div.Class("mt-1 flex flex-wrap gap-x-2 font-sans text-xs text-ui-muted sm:hidden")[
                            Span.Class("tabular-nums")[DashboardParts.Bytes(s.SizeBytes)],
                            Span.Title(s.CreatedAt.ToString("u"))[DashboardParts.Ago(s.CreatedAt, now)]
                        ]
                    ],
                    Td.Class("hidden whitespace-nowrap px-3 py-2 align-top tabular-nums sm:table-cell")[
                        DashboardParts.Bytes(s.SizeBytes)
                    ],
                    Td.Class("hidden whitespace-nowrap px-3 py-2 align-top text-xs text-ui-muted sm:table-cell")
                        .Title(s.CreatedAt.ToString("u"))[
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
                Thead.Class("border-b border-ui-line text-xs text-ui-muted")[
                    Tr[
                        Th.Class("px-3 py-2 font-medium")["Name"],
                        Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Every"],
                        Th.Class("hidden px-3 py-2 font-medium sm:table-cell")["Last enqueued"]
                    ]
                ],
                Tbody[_recurring.Select(r => Tr.Key(r.Name)
                    .Class("border-b border-ui-line/60 last:border-0")[
                    Td.Class($"w-full max-w-0 px-3 py-2 align-top {Ops.Mono}")[
                        Div.Class("break-all")[r.Name],
                        Div.Class("mt-1 flex flex-wrap items-center gap-x-2 font-sans text-xs text-ui-muted sm:hidden")[
                            Span[$"every {DashboardParts.Duration(r.Interval)}"],
                            r.LastEnqueuedAt is { } lastSmall
                                ? Span.Title(lastSmall.ToString("u"))[DashboardParts.Ago(lastSmall, now)]
                                : OpsBadge.Label("never")
                        ]
                    ],
                    Td.Class("hidden whitespace-nowrap px-3 py-2 align-top sm:table-cell")[DashboardParts.Duration(r.Interval)],
                    Td.Class("hidden whitespace-nowrap px-3 py-2 align-top sm:table-cell")[
                        r.LastEnqueuedAt is { } last
                            ? Span.Class("text-xs text-ui-muted").Title(last.ToString("u"))[
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
