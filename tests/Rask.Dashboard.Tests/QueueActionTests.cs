using Microsoft.EntityFrameworkCore;
using Rask.Jobs;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The actions run against tables a processor is actively draining, so each one's guard is the whole
/// safety argument. These tests pin the guards, not the happy path.
/// </summary>
public sealed class QueueActionTests
{
    [Fact]
    public async Task Retry_puts_a_dead_letter_back_in_the_queue()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;
        var id = await SeedAsync(h, Job(runAt: now.AddHours(-1), attempts: max, error: "boom"));

        var affected = await h.Queue("jobs").RetryAsync(id, CancellationToken.None);

        Assert.Equal(1, affected);
        var job = await SingleAsync(h);
        Assert.Equal(0, job.Attempts);      // eligible again
        Assert.Equal(now, job.RunAt);       // and due now, not after another backoff
        Assert.Null(job.Error);             // the stale error would otherwise read as a fresh failure
        Assert.Null(job.ProcessedAt);
    }

    [Fact]
    public async Task Retry_cannot_touch_a_row_the_processor_might_be_holding()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        // Due and not yet exhausted: exactly what the drain query selects. The guard is the inverse of
        // that query, so this row is invisible to retry — which is why the action needs no coordination
        // with a running processor.
        var id = await SeedAsync(h, Job(runAt: now.AddMinutes(-1), attempts: max - 1, error: "transient"));

        Assert.Equal(0, await h.Queue("jobs").RetryAsync(id, CancellationToken.None));

        var job = await SingleAsync(h);
        Assert.Equal(max - 1, job.Attempts);         // untouched
        Assert.Equal("transient", job.Error);
    }

    [Fact]
    public async Task Retry_cannot_resurrect_a_processed_row()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        // Attempts is high but it finished — re-running it would duplicate a side effect that already
        // happened, so ProcessedAt IS NULL is part of the guard rather than just Attempts >= max.
        var id = await SeedAsync(h, Job(runAt: now, attempts: max, processedAt: now));

        Assert.Equal(0, await h.Queue("jobs").RetryAsync(id, CancellationToken.None));
        Assert.NotNull((await SingleAsync(h)).ProcessedAt);
    }

    [Fact]
    public async Task RetryAll_takes_every_dead_letter_and_nothing_else()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        await SeedAsync(h,
            Job(runAt: now, attempts: max),          // dead
            Job(runAt: now, attempts: max),          // dead
            Job(runAt: now, attempts: max - 1),      // still retrying
            Job(runAt: now, processedAt: now));      // done

        Assert.Equal(2, await h.Queue("jobs").RetryAllAsync(CancellationToken.None));

        var counts = await h.Queue("jobs").CountsAsync(CancellationToken.None);
        Assert.Equal(0, counts.Failed);
        Assert.Equal(3, counts.Due);        // the two revived plus the one that was already retrying
        Assert.Equal(1, counts.Processed);
    }

    [Fact]
    public async Task PurgeProcessed_never_removes_outstanding_work_or_a_dead_letter()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var max = h.Get<JobOptions>().MaxAttempts;

        await SeedAsync(h,
            Job(runAt: now, processedAt: now.AddDays(-10)),   // old and done → goes
            Job(runAt: now, processedAt: now.AddHours(-1)),   // done but recent → stays
            Job(runAt: now, attempts: max),                   // dead letter → must survive
            Job(runAt: now));                                 // pending → must survive

        Assert.Equal(1, await h.Queue("jobs").PurgeProcessedAsync(TimeSpan.FromDays(7), CancellationToken.None));

        var counts = await h.Queue("jobs").CountsAsync(CancellationToken.None);
        Assert.Equal(1, counts.Failed);
        Assert.Equal(1, counts.Due);
        Assert.Equal(1, counts.Processed);
    }

    [Fact]
    public async Task Delete_removes_an_outstanding_row_but_never_a_processed_one()
    {
        await using var h = new DashboardHarness(Batteries.Jobs);
        var now = h.Clock.GetUtcNow().UtcDateTime;

        var pending = await SeedAsync(h, Job(runAt: now));
        var done = await SeedAsync(h, Job(runAt: now, processedAt: now));

        Assert.Equal(1, await h.Queue("jobs").DeleteAsync(pending, CancellationToken.None));

        // Deleting a completed row would erase the record of work that actually happened.
        Assert.Equal(0, await h.Queue("jobs").DeleteAsync(done, CancellationToken.None));

        await using var db = h.NewContext();
        Assert.Equal(done, (await db.Set<Job>().SingleAsync()).Id);
    }

    [Fact]
    public async Task Actions_on_an_unavailable_queue_are_no_ops()
    {
        // Registered but unmapped: every action must report zero rather than throw, matching how the
        // read side reports an unavailable panel as empty.
        await using var h = new DashboardHarness(registered: Batteries.All, mapped: Batteries.Jobs);
        var outbox = h.Queue("outbox");

        Assert.Equal(0, await outbox.RetryAsync(1, CancellationToken.None));
        Assert.Equal(0, await outbox.RetryAllAsync(CancellationToken.None));
        Assert.Equal(0, await outbox.PurgeProcessedAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.Equal(0, await outbox.DeleteAsync(1, CancellationToken.None));
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

    private static async Task<long> SeedAsync(DashboardHarness harness, Job job)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private static async Task SeedAsync(DashboardHarness harness, params Job[] jobs)
    {
        await using var db = harness.NewContext();
        db.Set<Job>().AddRange(jobs);
        await db.SaveChangesAsync();
    }

    private static async Task<Job> SingleAsync(DashboardHarness harness)
    {
        await using var db = harness.NewContext();
        return await db.Set<Job>().SingleAsync();
    }
}
