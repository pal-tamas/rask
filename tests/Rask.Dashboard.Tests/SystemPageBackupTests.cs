using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Pages;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The Backup card's restorability tile. "The replicator is running" and "the backup can be restored" are
/// different facts, and the card has to show them as different facts — a green replication tile beside a
/// broken restore is the state this whole surface exists to make visible.
/// </summary>
public sealed class SystemPageBackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Shows_nothing_about_restorability_when_no_pass_has_run()
    {
        // Verification is opt-in, so "no reading" is the normal case — and it must not render as good news.
        await using var harness = Harness(new FakeBackupProbe(null));

        var html = Render(harness);

        Assert.Contains("Continuous replication", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Last verified restore", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_when_the_backup_was_last_proven_restorable()
    {
        await using var harness = Harness(new FakeBackupProbe(new BackupVerificationInfo(
            "Verified", BackupVerificationLevel.Verified, Now.AddHours(-2), null)));

        var html = Render(harness);

        Assert.Contains("Last verified restore", html, StringComparison.Ordinal);
        Assert.Contains("restorable", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shows_a_failed_restore_beside_a_healthy_replicator()
    {
        // The exact blind spot: the replica cannot be read back, and every replication signal is fine.
        await using var harness = Harness(new FakeBackupProbe(
            new BackupVerificationInfo("Failed", BackupVerificationLevel.Broken, null, "litestream restore failed with exit code 1."),
            replicating: true));

        var page = Rendered(harness);

        Assert.Contains("running", page.Html, StringComparison.Ordinal);         // replication looks healthy…
        Assert.Contains("Last verified restore", page.Html, StringComparison.Ordinal);
        // The tone, not the word: "failed" also appears in the error text below the tile, so asserting on
        // it alone would pass even if the tile itself never rendered. RestartCount is 0 here, so nothing
        // else on the card is red.
        Assert.Contains("red", BackupTones(page));
        // And red ONLY — an amber tile here would mean the level collapsed to a single "something is off".
        Assert.DoesNotContain("amber", BackupTones(page));
        Assert.Contains("exit code 1", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_lag_as_unproven_rather_than_broken()
    {
        await using var harness = Harness(new FakeBackupProbe(new BackupVerificationInfo(
            "Inconclusive", BackupVerificationLevel.Unknown, Now.AddDays(-1), "the sentinel had not reached the replica")));

        var page = Rendered(harness);

        Assert.Contains("inconclusive", page.Html, StringComparison.Ordinal);
        // Warning, never danger: a tile that goes red every time the check races replication is a tile
        // operators learn to ignore.
        Assert.Contains("amber", BackupTones(page));
        Assert.DoesNotContain("red", BackupTones(page));
    }

    private static DashboardHarness Harness(FakeBackupProbe probe) =>
        new(Batteries.None, extra: services => services.AddSingleton<IDashboardBackupProbe>(probe));

    // The page reads its backup state on PollingPanel's asynchronous mount, so the first render is the
    // placeholder — wait for the card rather than assert on markup that has not loaded yet.
    private static string Render(DashboardHarness harness) => Rendered(harness).Html;

    private static Page Rendered(DashboardHarness harness)
    {
        var page = Page.Render(ActivatorUtilities.CreateInstance<SystemPage>(harness.Services), harness.Services);
        page.Shows("Backup");
        return page;
    }

    // The tones on the Backup card alone. The Database card above it describes the harness's own database, which is
    // in memory: its journal mode is MEMORY, not WAL, and that fact is rightly amber — but it is not the backup's.
    private static List<string> BackupTones(Page page)
    {
        var card = page.FindAll("h2").Single(h => h.TextContent.Trim() == "Backup");
        while (card.Parent is { } parent && !card.TextContent.Contains("Continuous replication", StringComparison.Ordinal))
        {
            card = parent;
        }

        return [.. card.DescendantsAndSelf().Select(n => n.Attribute("data-color")).OfType<string>()];
    }

    private sealed class FakeBackupProbe(BackupVerificationInfo? verification, bool replicating = true)
        : IDashboardBackupProbe
    {
        public Task<BackupReplicationInfo?> Replication(CancellationToken cancellationToken) =>
            Task.FromResult<BackupReplicationInfo?>(new BackupReplicationInfo(replicating, Now.AddDays(-3), 0, null));

        public Task<IReadOnlyList<BackupSnapshotInfo>> Snapshots(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackupSnapshotInfo>>([]);

        public Task<BackupVerificationInfo?> Verification(CancellationToken cancellationToken) =>
            Task.FromResult(verification);
    }
}
