using System.Globalization;
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
    OpsOptions options,
    TimeProvider timeProvider) : PollingPanel
{
    private DatabaseInfo? _database;
    private IReadOnlyList<RecurringJobRow> _recurring = [];
    private BackupReplicationInfo? _replication;
    private BackupVerificationInfo? _verification;
    private IReadOnlyList<BackupSnapshotInfo> _snapshots = [];

    /// <inheritdoc />
    protected override OpsOptions Options => options;

    /// <inheritdoc />
    protected override async Task<object?> Load(CancellationToken cancellationToken)
    {
        _database = await system.Database(cancellationToken).ConfigureAwait(false);
        _recurring = await system.RecurringJobs(cancellationToken).ConfigureAwait(false);
        _replication = await system.Replication(cancellationToken).ConfigureAwait(false);
        _verification = await system.Verification(cancellationToken).ConfigureAwait(false);
        _snapshots = await system.Snapshots(cancellationToken).ConfigureAwait(false);

        return string.Join('|',
            [$"{_database?.SizeBytes}:{_database?.JournalMode}:{_database?.ForeignKeys}",
             $"{_replication?.IsReplicating}:{_replication?.RestartCount}:{_replication?.LastError}",
             $"{_verification?.Outcome}:{_verification?.LastVerifiedAt?.Ticks ?? 0}:{_verification?.LastError}",
             $"{_snapshots.Count}:{(_snapshots.Count > 0 ? _snapshots[0].Name : null)}",
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
            DashboardHeading.Title("System"),
            DashboardError.Message(LoadError),
            DatabaseCard(),
            BackupCards(now),
            RecurringCard(now),
            DashboardParked.Parked(IsParked).Resume(Resume),
        ];
    }

    private Component? DatabaseCard()
    {
        if (_database is not { } db)
        {
            return null;
        }

        // A list of facts rather than four figures. These are four short scalars an operator reads once to confirm
        // the deployment is configured the way they think — a headline number's worth of weight each was three
        // times the space and none of the extra meaning.
        return Ui.Card[Ui.CardHeader[Ui.CardHeading.Level(2)["Database"]], Ui.CardBody[
            DashboardFacts[
                DashboardFact
                    .Key("size")
                    .Label("Size")
                    .Value(db.SizeBytes is { } size ? DashboardParts.Bytes(size) : "—")
                    .Mono(true),
                DashboardFact
                    .Key("journal")
                    .Label("Journal mode")
                    .Value(db.JournalMode?.ToUpperInvariant() ?? "n/a")
                    .Mono(true)
                    // WAL is the mode every Rask deployment expects; anything else is worth noticing.
                    .Warning(db.JournalMode is not null
                             && !db.JournalMode.Equals("wal", StringComparison.OrdinalIgnoreCase)),
                DashboardFact
                    .Key("fks")
                    .Label("Foreign keys")
                    .Value(db.ForeignKeys switch { true => "on", false => "off", null => "n/a" })
                    .Mono(true)
                    .Warning(db.ForeignKeys is false),
                DashboardFact
                    .Key("provider")
                    .Label("Provider")
                    .Value(ShortProvider(db.Provider))
                    .Mono(true)
            ]
        ]];
    }

    // One grid of every backup fact, then the snapshots as a card of their own. A card does not space the
    // sections inside it, and the column the cards sit in does — so two sections are two cards.
    private Component? BackupCards(DateTime now)
    {
        // No probe registered means the app didn't say how it backs up — showing "no backups" would be a
        // claim the dashboard can't support, so the cards stay away entirely.
        if (!system.HasBackupProbe)
        {
            return null;
        }

        // A probe that only takes snapshots has no replication or verification to report, and a Backup card with a
        // heading and nothing under it reads as a broken panel rather than as "not applicable".
        var stats = BackupStats(now).ToList();

        return [
            stats.Count == 0
                ? null
                : Ui.Card.Key("backup")[
                    Ui.CardHeader[Ui.CardHeading.Level(2)["Backup"]],
                    Ui.CardBody[Div.Class("grid gap-6 sm:grid-cols-3")[stats]]
                ],
            Ui.Card.Key("snapshots")[Ui.CardHeader[Ui.CardHeading.Level(2)["Snapshots"]], Ui.CardBody[SnapshotList(now)]]
        ];
    }

    private IEnumerable<Component> BackupStats(DateTime now)
    {
        if (_replication is { } r)
        {
            yield return DashboardMetric
                .Key("replication")
                .Label("Continuous replication")
                .Value(r.IsReplicating ? "running" : "stopped")
                .Danger(!r.IsReplicating)
                .Caption(r.LastStartedAt is { } started
                    ? $"since {DashboardParts.Ago(started.UtcDateTime, now)}"
                    : "never started")
                .Icon(Ui.IconName.ArrowPath)
                .Bare(true);

            yield return DashboardMetric
                .Key("restarts")
                .Label("Restarts")
                .Value(r.RestartCount.ToString(CultureInfo.CurrentCulture))
                .Warning(r.RestartCount > 0)
                .Caption(r.LastError ?? "no failures recorded")
                .Icon(Ui.IconName.ExclamationTriangle)
                .Bare(true);
        }

        // Restorability is its own fact: "the replicator is running" says nothing about whether what it
        // wrote can be read back.
        if (_verification is { } v)
        {
            yield return DashboardMetric
                .Key("verification")
                .Label("Last verified restore")
                .Value(v.Level == BackupVerificationLevel.Verified
                    ? "restorable"
                    : v.Outcome.ToLowerInvariant())
                // Broken is red; Unknown is amber. A check that races replication must not paint the tile
                // red, or the tile stops being read.
                .Danger(v.Level == BackupVerificationLevel.Broken)
                .Warning(v.Level is not (BackupVerificationLevel.Verified or BackupVerificationLevel.Broken))
                .Caption(v.LastVerifiedAt is { } verified
                    ? $"verified {DashboardParts.Ago(verified.UtcDateTime, now)}"
                    : v.LastError ?? "never verified")
                .Icon(v.Level == BackupVerificationLevel.Broken
                    ? Ui.IconName.ShieldExclamation
                    : Ui.IconName.ShieldCheck)
                .Bare(true);
        }
    }

    private Component SnapshotList(DateTime now) =>
        _snapshots.Count == 0
            ? DashboardEmpty.Title("No snapshots stored")
            : Ui.Table.Class("w-full min-w-120").Bleed(true).AriaLabel("Newest snapshots")[
                Ui.TableColumns[
                    Ui.TableColumn["Snapshot"],
                    Ui.TableColumn.Class("w-28")["Size"],
                    Ui.TableColumn.Class("w-28")["Taken"]
                ],
                Ui.TableRows[
                    _snapshots.Take(10).Select(s => Ui.TableRow.Key(s.Name)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate font-mono").Title(s.Name)[s.Name],
                        Ui.TableCell[DashboardParts.Bytes(s.SizeBytes)],
                        Ui.TableCell[Span.Title(s.CreatedAt.ToString("u"))[DashboardParts.Ago(s.CreatedAt, now)]]
                    ])
                ]
            ];

    private Component? RecurringCard(DateTime now)
    {
        if (_recurring.Count == 0)
        {
            return null;
        }

        return Ui.Card[Ui.CardHeader[Ui.CardHeading.Level(2)["Recurring jobs"]], Ui.CardBody[
            Ui.Table.Class("w-full min-w-140").Bleed(true).AriaLabel("Recurring jobs")[
                Ui.TableColumns[
                    Ui.TableColumn["Name"],
                    Ui.TableColumn.Class("w-40")["Schedule"],
                    Ui.TableColumn.Class("w-32")["Last enqueued"]
                ],
                Ui.TableRows[
                    _recurring.Select(r => Ui.TableRow.Key(r.Name)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate font-mono").Title(r.Name)[r.Name],
                        Ui.TableCell.Class("truncate font-mono").Title(r.Schedule)[r.Schedule],
                        Ui.TableCell[
                            r.LastEnqueuedAt is { } last
                                ? Span.Title(last.ToString("u"))[DashboardParts.Ago(last, now)]
                                // Declared but never fired: either the app just started, or this one is stuck.
                                : Ui.Badge.Sm["never"]
                        ]
                    ])
                ]
            ]
        ]];
    }

    // "Microsoft.EntityFrameworkCore.Sqlite" reads better as "Sqlite" in a tile.
    private static string ShortProvider(string provider) =>
        provider.Split('.').LastOrDefault() is { Length: > 0 } last ? last : provider;
}
