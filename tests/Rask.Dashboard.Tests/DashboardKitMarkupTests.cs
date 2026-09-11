using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Pages;
using Rask.Jobs;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     What the pages draw with the kit: the facts that used to live in hand-written class strings and now
///     live in typed steps — a dead letter's tone, and a queue card that is one link.
/// </summary>
public sealed class DashboardKitMarkupTests
{
    [Fact]
    public async Task A_dead_letter_row_carries_the_error_tone()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SaveAsync(h, new Job
        {
            Type = "Some.Job",
            Payload = "{}",
            RunAt = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            Attempts = h.Get<JobOptions>().MaxAttempts,   // out of attempts and unprocessed: a dead letter
            Error = "boom",
        });

        var component = ActivatorUtilities.CreateInstance<QueuePage>(h.Services);
        component.Queue = "jobs";
        component.Show = "failed";
        var page = RaskTest.Render(component, h.Services);
        await page.WaitForAsync("dead letter");

        var row = Assert.Single(page.FindAll("tbody tr"));
        Assert.Contains("bg-error/10", row.Attribute("class") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_queue_card_on_the_overview_is_one_link_to_its_queue()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);

        var component = ActivatorUtilities.CreateInstance<OverviewPage>(h.Services);
        var page = RaskTest.Render(component, h.Services);
        await page.WaitForAsync("Outstanding");

        // One link, holding the figures rather than sitting beside them.
        var card = Assert.Single(page.FindAll("a"), a =>
            (a.Attribute("href") ?? "").EndsWith("/queues/jobs", StringComparison.Ordinal));
        Assert.Contains("Outstanding", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Failed", card.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Queue_row_buttons_are_keyed_siblings_with_no_text_between_them()
    {
        // Unkeyed text among keyed buttons forces the diff to match by position, so a focused Retry could be
        // patched into Delete. The grid cell spaces the buttons instead.
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            configure: o => o.Actions = RaskDashboardActions.Safe | RaskDashboardActions.Destructive);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SaveAsync(h, new Job
        {
            Type = "Some.Job",
            Payload = "{}",
            RunAt = now.AddHours(-1),
            CreatedAt = now.AddHours(-1),
            Attempts = h.Get<JobOptions>().MaxAttempts,
            Error = "boom",
        });

        var component = ActivatorUtilities.CreateInstance<QueuePage>(h.Services);
        component.Queue = "jobs";
        component.Show = "failed";
        var page = RaskTest.Render(component, h.Services);
        await page.WaitForAsync("dead letter");

        Assert.Contains("</button><button", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("</button> <button", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_probe_that_only_takes_snapshots_draws_no_empty_backup_card()
    {
        await using var h = new DashboardHarness(
            Batteries.None,
            extra: services => services.AddSingleton<IDashboardBackupProbe>(new SnapshotsOnlyProbe()));

        var html = await RaskTest.Render(ActivatorUtilities.CreateInstance<SystemPage>(h.Services), h.Services)
            .WaitForAsync("Snapshots");

        Assert.DoesNotContain(">Backup<", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 0)]
    [InlineData(25, 25, 0)]
    [InlineData(26, 25, 1)]
    [InlineData(51, 25, 2)]
    public void The_last_page_is_counted_from_the_total(int total, int pageSize, int expected) =>
        Assert.Equal(expected, DashboardParts.LastPageIndex(total, pageSize));

    private sealed class SnapshotsOnlyProbe : IDashboardBackupProbe
    {
        public Task<BackupReplicationInfo?> ReplicationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<BackupReplicationInfo?>(null);

        public Task<IReadOnlyList<BackupSnapshotInfo>> SnapshotsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BackupSnapshotInfo>>([]);

        public Task<BackupVerificationInfo?> VerificationAsync(CancellationToken cancellationToken) =>
            Task.FromResult<BackupVerificationInfo?>(null);
    }

    private static async Task SaveAsync(DashboardHarness harness, Job job)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
    }
}
