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
            UiHeader.Heading("System"),
            DashboardError.Message(LoadError),
            DatabaseCard(),
            BackupCards(now),
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
        return UiCard.Heading("Database")[
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
                        ? UiTone.Warning
                        : null),
                UiDetailRow
                    .Key("fks")
                    .Label("Foreign keys")
                    .Value(db.ForeignKeys switch { true => "on", false => "off", null => "n/a" })
                    .Mono(true)
                    .Tone(db.ForeignKeys is false ? UiTone.Warning : null),
                UiDetailRow
                    .Key("provider")
                    .Label("Provider")
                    .Value(ShortProvider(db.Provider))
                    .Mono(true)
            ]
        ];
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

        return [
            UiCard.Key("backup").Heading("Backup")[UiGrid[BackupStats(now)]],
            UiCard.Key("snapshots").Heading("Snapshots")[SnapshotList(now)]
        ];
    }

    private IEnumerable<Component> BackupStats(DateTime now)
    {
        if (_replication is { } r)
        {
            yield return UiStat
                .Key("replication")
                .Value(r.IsReplicating ? "running" : "stopped")
                .Label("Continuous replication")
                .Tone(r.IsReplicating ? null : UiTone.Error)
                .Caption(r.LastStartedAt is { } started
                    ? $"since {DashboardParts.Ago(started.UtcDateTime, now)}"
                    : "never started")
                .Icon(UiIconName.Retry);

            yield return UiStat
                .Key("restarts")
                .Value(r.RestartCount.ToString())
                .Label("Restarts")
                .Tone(r.RestartCount > 0 ? UiTone.Warning : null)
                .Caption(r.LastError ?? "no failures recorded")
                .Icon(UiIconName.Warning);
        }

        // Restorability is its own fact: "the replicator is running" says nothing about whether what it
        // wrote can be read back.
        if (_verification is { } v)
        {
            yield return UiStat
                .Key("verification")
                .Value(v.Level == BackupVerificationLevel.Verified
                    ? "restorable"
                    : v.Outcome.ToLowerInvariant())
                .Label("Last verified restore")
                // Broken is red; Unknown is amber. A check that races replication must not paint the tile
                // red, or the tile stops being read.
                .Tone(v.Level switch
                {
                    BackupVerificationLevel.Verified => null,
                    BackupVerificationLevel.Broken => UiTone.Error,
                    _ => UiTone.Warning,
                })
                .Caption(v.LastVerifiedAt is { } verified
                    ? $"verified {DashboardParts.Ago(verified.UtcDateTime, now)}"
                    : v.LastError ?? "never verified")
                .Icon(v.Level == BackupVerificationLevel.Broken
                    ? UiIconName.ShieldWarning
                    : UiIconName.ShieldOk);
        }
    }

    private Component SnapshotList(DateTime now) =>
        _snapshots.Count == 0
            ? UiEmpty.Heading("No snapshots stored")
            : UiDataGrid.Data(_snapshots.Take(10).ToList()).RowKey(s => s.Name).Label("Newest snapshots")[c => [
                c.Field(s => s.Name).Title("Snapshot").Mono(true),
                c.Field(s => s.SizeBytes).Title("Size").Value(s => DashboardParts.Bytes(s.SizeBytes)),
                c.Field(s => s.CreatedAt).Title("Taken").Cell(s =>
                    Span.Title(s.CreatedAt.ToString("u"))[DashboardParts.Ago(s.CreatedAt, now)]),
            ]];

    private Component? RecurringCard(DateTime now)
    {
        if (_recurring.Count == 0)
        {
            return null;
        }

        return UiCard.Heading("Recurring jobs")[
            UiDataGrid.Data(_recurring).RowKey(r => r.Name).Label("Recurring jobs")[c => [
                c.Field(r => r.Name).Title("Name").Mono(true),
                c.Field(r => r.Interval).Title("Every").Value(r => DashboardParts.Duration(r.Interval)),
                c.Field(r => r.LastEnqueuedAt).Title("Last enqueued").Cell(r =>
                    r.LastEnqueuedAt is { } last
                        ? Span.Title(last.ToString("u"))[DashboardParts.Ago(last, now)]
                        // Declared but never fired: either the app just started, or this one is stuck.
                        : UiBadge["never"]),
            ]]
        ];
    }

    // "Microsoft.EntityFrameworkCore.Sqlite" reads better as "Sqlite" in a tile.
    private static string ShortProvider(string provider) =>
        provider.Split('.').LastOrDefault() is { Length: > 0 } last ? last : provider;
}
