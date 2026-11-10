using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Dashboard.Pages;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The jobs queue says what runs right now, in which lane, and can stop it: a Running slice, a Lane column, a Stop row
/// button behind a confirmation, and the stopped statuses.
/// </summary>
public sealed class JobsRunningSliceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_running_job_is_counted_as_running_and_not_as_due_or_failed()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobsOptions>().MaxAttempts;
        await SeedAsync(h,
            QueueRowBuilder.Running(now.AddMinutes(-12), now.AddMinutes(3), "pump-1"),
            QueueRowBuilder.Running(now.AddMinutes(-1), now.AddMinutes(3), "pump-2", attempts: max), // its last attempt
            QueueRowBuilder.Running(now.AddMinutes(-30), now.AddMinutes(-1), "pump-3"),             // claim ran out: due again
            QueueRowBuilder.Job(runAt: now.AddMinutes(-1)));

        var counts = await h.Queue("jobs").Counts(Ct);

        Assert.Equal(2, counts.Running);
        Assert.Equal(2, counts.Due);
        Assert.Equal(0, counts.Failed);
        Assert.Equal(4, counts.Outstanding);
    }

    [Fact]
    public async Task The_running_slice_lists_each_running_job_with_its_lane_and_when_it_started()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h,
            QueueRowBuilder.Running(now.AddMinutes(-12), now.AddMinutes(3), "pump-1", stopped: JobStop.Asked),
            QueueRowBuilder.Job(runAt: now.AddMinutes(-1)));

        var (rows, total) = await h.Queue("jobs").Page(QueueFilter.Running, 0, 25, Ct);

        Assert.Equal(1, total);
        var row = Assert.Single(rows);
        Assert.Equal("pump-1", row.Lane);
        Assert.Equal(now.AddMinutes(-12), row.StartedAt);
        Assert.Equal(JobStop.Asked, row.Stopped);
        Assert.True(row.IsRunning(now));
    }

    [Fact]
    public async Task Mail_and_the_outbox_never_count_anything_as_running()
    {
        await using var h = new DashboardHarness(Batteries.Outbox);

        var counts = await h.Queue("outbox").Counts(Ct);
        var (rows, _) = await h.Queue("outbox").Page(QueueFilter.Running, 0, 25, Ct);

        Assert.Equal(0, counts.Running);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Retry_never_resets_a_job_whose_last_attempt_is_running()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobsOptions>().MaxAttempts;
        await SeedAsync(h, QueueRowBuilder.Running(now.AddMinutes(-1), now.AddMinutes(3), "pump-1", attempts: max));

        var retried = await h.Queue("jobs").RetryAll(Ct);

        Assert.Equal(0, retried);
        Assert.Equal(max, (await SingleAsync(h)).Attempts);
    }

    [Fact]
    public async Task The_page_shows_the_running_slice_the_lane_and_a_stop_that_asks_first()
    {
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            configure: o => o.Actions = RaskDashboardActions.All,
            extra: s => s.AddScoped<Rask.Core.Messaging.IToaster, Rask.Core.Messaging.Toaster>());
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, QueueRowBuilder.Running(now.AddMinutes(-12), now.AddMinutes(3), "pump-1"));

        var page = Render(h, "running");
        page.Shows("pump-1");

        Assert.Contains("Running", page.Html, StringComparison.Ordinal);
        Assert.Contains("Lane", page.Html, StringComparison.Ordinal);
        Assert.Contains("started 12m ago", page.Html, StringComparison.Ordinal);
        Assert.Contains(">running<", page.Html, StringComparison.Ordinal);
        await Click(page, "Stop");
        Assert.Contains("Stop this job? It ends as stopped and is not tried again.", page.Html, StringComparison.Ordinal);
        Assert.Null((await SingleAsync(h)).Stopped); // asked, not yet confirmed
    }

    [Fact]
    public async Task Confirming_the_stop_of_a_running_job_marks_it_stopping()
    {
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            configure: o => o.Actions = RaskDashboardActions.All,
            extra: s => s.AddScoped<Rask.Core.Messaging.IToaster, Rask.Core.Messaging.Toaster>());
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, QueueRowBuilder.Running(now.AddMinutes(-12), now.AddMinutes(3), "pump-1"));
        var page = Render(h, "running");
        page.Shows("pump-1");

        await Click(page, "Stop");
        await Click(page, "Confirm");
        page.Shows("stopping");

        var job = await SingleAsync(h);
        Assert.Equal(JobStop.Asked, job.Stopped);
        Assert.Null(job.ProcessedAt); // its host ends it; the console only asked
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Trim() == "Stop");
    }

    [Fact]
    public async Task Stopping_a_waiting_job_ends_it_at_once()
    {
        await using var h = new DashboardHarness(
            Batteries.Jobs,
            configure: o => o.Actions = RaskDashboardActions.All,
            extra: s => s.AddScoped<Rask.Core.Messaging.IToaster, Rask.Core.Messaging.Toaster>());
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, QueueRowBuilder.Job(runAt: now.AddMinutes(5)));
        var page = Render(h, "delayed");
        page.Shows("Stop");

        await Click(page, "Stop");
        await Click(page, "Confirm");

        var job = await SingleAsync(h);
        Assert.Equal(JobStop.Asked, job.Stopped);
        Assert.NotNull(job.ProcessedAt);
    }

    [Fact]
    public async Task Without_the_destructive_tier_there_is_no_stop()
    {
        await using var h = new DashboardHarness(Batteries.Jobs, configure: o => o.Actions = RaskDashboardActions.Safe);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, QueueRowBuilder.Running(now.AddMinutes(-12), now.AddMinutes(3), "pump-1"));

        var page = Render(h, "running");
        page.Shows("pump-1");

        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Trim() == "Stop");
    }

    [Theory]
    [InlineData(JobStop.Asked, "stopped")]
    [InlineData(JobStop.TimeLimit, "stopped: time limit")]
    [InlineData(JobStop.Idle, "stopped: no progress")]
    [InlineData(JobStop.DoneElsewhere, "stopped: done elsewhere")]
    public async Task A_processed_job_that_was_stopped_says_why(JobStop why, string status)
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedAsync(h, QueueRowBuilder.EndedStopped(now.AddMinutes(-1), why));

        var page = Render(h, "processed");

        page.Shows(status);
    }

    private static async Task SeedAsync(DashboardHarness h, params Job[] jobs)
    {
        await using var db = h.NewContext();
        db.Set<Job>().AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private static async Task<Job> SingleAsync(DashboardHarness h)
    {
        await using var db = h.NewContext();
        return await db.Set<Job>().AsNoTracking().SingleAsync();
    }

    private static Page<QueuePage> Render(DashboardHarness h, string show)
    {
        var component = ActivatorUtilities.CreateInstance<QueuePage>(h.Services);
        component.Queue = "jobs";
        component.Show = show;
        return Page.Render(component, h.Services);
    }

    private static Task Click(Page<QueuePage> page, string label)
    {
        var button = page.FindAll("button").First(b => b.TextContent.Trim() == label);
        var handler = button.Attribute("data-rask-on-click");
        Assert.NotNull(handler);
        return page.Invoke(handler);
    }
}
