using Microsoft.EntityFrameworkCore;
using Rask.Jobs;
using Rask.Outbox;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The counts are the product. "Failed" in particular has to mean exactly what the processors mean by
/// giving up, or the dashboard reports a healthy system while a queue retries itself to death.
/// </summary>
public sealed class QueuePanelTests
{
    [Fact]
    public async Task Counts_split_a_queue_into_due_delayed_failed_and_processed()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        await SeedJobsAsync(h,
            Job(runAt: now.AddMinutes(-1)),                       // due
            Job(runAt: now.AddMinutes(-1)),                       // due
            Job(runAt: now.AddMinutes(5)),                        // delayed (backoff or schedule)
            Job(runAt: now.AddMinutes(-1), attempts: max),        // dead letter
            Job(runAt: now.AddMinutes(-1), processedAt: now));    // done

        var counts = await h.Queue("jobs").CountsAsync(CancellationToken.None);

        Assert.Equal(2, counts.Due);
        Assert.Equal(1, counts.Delayed);
        Assert.Equal(1, counts.Failed);
        Assert.Equal(1, counts.Processed);
        Assert.Equal(4, counts.Outstanding);   // everything not processed, dead letters included
    }

    [Fact]
    public async Task A_row_is_failed_only_once_it_is_out_of_attempts()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        // One attempt short is still a retry, not a dead letter — the boundary the processors use.
        await SeedJobsAsync(h,
            Job(runAt: now.AddMinutes(-1), attempts: max - 1),
            Job(runAt: now.AddMinutes(-1), attempts: max));

        var counts = await h.Queue("jobs").CountsAsync(CancellationToken.None);

        Assert.Equal(1, counts.Failed);
        Assert.Equal(1, counts.Due);
    }

    [Fact]
    public async Task The_failed_filter_returns_exactly_the_dead_letters()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        await SeedJobsAsync(h,
            Job(runAt: now, attempts: max, error: "boom"),
            Job(runAt: now));

        var (rows, total) = await h.Queue("jobs").PageAsync(QueueFilter.Failed, 0, 25, CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Equal("boom", Assert.Single(rows).Error);
    }

    [Fact]
    public async Task Paging_reports_the_total_behind_the_page()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await SeedJobsAsync(h, [.. Enumerable.Range(0, 7).Select(_ => Job(runAt: now))]);

        var (rows, total) = await h.Queue("jobs").PageAsync(QueueFilter.Outstanding, 0, 3, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal(7, total);   // the pager needs what's behind the page, not the page size
    }

    [Fact]
    public async Task An_unregistered_battery_is_unavailable_and_reads_as_empty()
    {
        // Jobs only: the outbox panel is registered but its battery is not.
        await using var h = new DashboardHarness(Batteries.Jobs);

        var outbox = h.Queue("outbox");
        Assert.False(outbox.IsAvailable);

        // And it must not throw when something asks anyway — an unavailable panel reads as nothing.
        Assert.Equal(default, await outbox.CountsAsync(CancellationToken.None));
        Assert.Empty((await outbox.PageAsync(QueueFilter.Outstanding, 0, 25, CancellationToken.None)).Rows);
    }

    [Fact]
    public async Task A_registered_battery_whose_table_is_not_mapped_is_unavailable()
    {
        // The trap this guards: AddRaskOutbox() called, modelBuilder.AddRaskOutbox() forgotten. The
        // service resolves, so a registration-only probe would call it available and every query would
        // throw. Reporting "not here" is the only honest answer.
        await using var h = new DashboardHarness(registered: Batteries.All, mapped: Batteries.Jobs);

        Assert.True(h.Queue("jobs").IsAvailable);
        Assert.False(h.Queue("outbox").IsAvailable);
        Assert.False(h.Queue("mail").IsAvailable);
    }

    [Fact]
    public async Task The_outbox_panel_maps_OccurredAt_onto_the_shared_shape()
    {
        await using var h = new DashboardHarness(Batteries.Outbox);
        var occurred = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-3);

        await using (var db = h.NewContext())
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Type = "Some.Event",
                Payload = "{}",
                OccurredAt = occurred,
            });
            await db.SaveChangesAsync();
        }

        var row = Assert.Single(
            (await h.Queue("outbox").PageAsync(QueueFilter.Outstanding, 0, 25, CancellationToken.None)).Rows);

        Assert.Equal(occurred, row.CreatedAt);
        // The outbox has no scheduled-run column, so RunAt mirrors OccurredAt — which correctly leaves
        // its Delayed count permanently zero rather than inventing a delay it doesn't have.
        Assert.Equal(occurred, row.RunAt);
        Assert.Equal(0, (await h.Queue("outbox").CountsAsync(CancellationToken.None)).Delayed);
    }

    private static Job Job(DateTime runAt, int attempts = 0, DateTime? processedAt = null, string? error = null) =>
        new()
        {
            Type = "Some.Job",
            Payload = "{}",
            RunAt = runAt,
            CreatedAt = runAt,
            Attempts = attempts,
            ProcessedAt = processedAt,
            Error = error,
        };

    private static async Task SeedJobsAsync(DashboardHarness harness, params Job[] jobs)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().AddRange(jobs);
        await db.SaveChangesAsync();
    }
}
