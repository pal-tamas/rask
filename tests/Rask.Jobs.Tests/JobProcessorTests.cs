using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs.Tests;

public sealed class JobProcessorTests
{
    [Fact]
    public async Task Enqueue_runs_the_handler_and_marks_the_job_processed()
    {
        await using var h = new JobsHarness();
        await h.Queue.EnqueueAsync(new RecordJob("hello"));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().AnyAsync(j => j.ProcessedAt != null);
            });
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains("hello", h.Recorder.Values);
        Assert.NotNull((await h.SingleJobAsync()).ProcessedAt);
    }

    [Fact]
    public async Task A_job_in_a_keyword_namespace_is_delivered_not_dead_lettered()
    {
        await using var h = new JobsHarness();
        await h.Queue.EnqueueAsync(new @event.KeywordJob("kw"));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().AnyAsync(j => j.ProcessedAt != null);
            });
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        var job = await h.SingleJobAsync();

        // Delivered — and, the load-bearing half, delivered without a failed attempt. A key miss doesn't
        // throw: it records "No registered job type '...'" and retries until MaxAttempts, so asserting
        // only on ProcessedAt would miss the bug entirely.
        Assert.NotNull(job.ProcessedAt);
        Assert.Null(job.Error);
        Assert.Equal(1, job.Attempts); // attempts *started* — one claim, no failure (see Job.Attempts)
        Assert.DoesNotContain('@', job.Type); // stored as the runtime name, unescaped
        Assert.Contains("kw", h.Recorder.Values); // the handler really ran
    }

    [Fact]
    public async Task A_scheduled_job_does_not_run_before_its_run_at()
    {
        await using var h = new JobsHarness();
        await h.Queue.ScheduleAsync(new RecordJob("later"), TimeSpan.FromHours(1));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(250); // several poll ticks at the current (frozen) time
            Assert.DoesNotContain("later", h.Recorder.Values); // not due yet

            h.Clock.Advance(TimeSpan.FromHours(1));
            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Values.Contains("later")));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_failing_job_retries_with_backoff_and_dead_letters_at_max_attempts()
    {
        await using var h = new JobsHarness(o =>
        {
            o.MaxAttempts = 3;
            o.BaseRetryDelay = TimeSpan.FromMinutes(1);
        });
        await h.Queue.EnqueueAsync(new FailingJob());

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            // Wait on the recorded failure, not on Attempts: the claim increments Attempts *before* the job
            // runs, so `Attempts >= 1` is already true while the attempt is still in flight — advancing the
            // clock then would race the failure that sets the next RunAt.
            await h.WaitUntilAsync(async () => await FailedAttemptsAsync(h) >= 1);

            // Frozen clock ⇒ the backed-off retry isn't due; advancing time triggers each next attempt.
            h.Clock.Advance(TimeSpan.FromMinutes(5));
            await h.WaitUntilAsync(async () => await FailedAttemptsAsync(h) >= 2);

            h.Clock.Advance(TimeSpan.FromMinutes(5));
            await h.WaitUntilAsync(async () => await FailedAttemptsAsync(h) >= 3);

            // Dead-lettered at MaxAttempts: further time passing does not attempt it again.
            h.Clock.Advance(TimeSpan.FromHours(1));
            await Task.Delay(250);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        var job = await h.SingleJobAsync();
        Assert.Equal(3, job.Attempts);
        Assert.Null(job.ProcessedAt);
        Assert.NotNull(job.Error);
    }

    /// <summary>
    /// How many attempts have finished and failed. <see cref="Job.Attempts"/> counts attempts *started*
    /// (the claim increments it, so a job that kills the process still counts toward MaxAttempts), so a
    /// recorded <see cref="Job.Error"/> is what says the attempt is over.
    /// </summary>
    private static async Task<int> FailedAttemptsAsync(JobsHarness h)
    {
        var job = await h.SingleJobAsync();
        return job.Error is null ? job.Attempts - 1 : job.Attempts;
    }

    [Fact]
    public async Task A_recurring_job_enqueues_once_per_interval()
    {
        await using var h = new JobsHarness(o =>
            o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Ticks >= 1)); // due immediately
            await Task.Delay(250);
            Assert.Equal(1, h.Recorder.Ticks); // not due again within the interval

            h.Clock.Advance(TimeSpan.FromHours(1));
            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Ticks >= 2));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_restart_within_the_interval_does_not_re_enqueue_a_recurring_job()
    {
        await using var first = new JobsHarness(o =>
            o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()));

        await first.Processor.StartAsync(CancellationToken.None);
        await first.WaitUntilAsync(() => Task.FromResult(first.Recorder.Ticks >= 1));
        await first.Processor.StopAsync(CancellationToken.None);

        // "Restart": a fresh processor over the SAME database at the same (still-within-interval) time.
        await using var restarted = new JobsHarness(
            o => o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()),
            start: first.Clock.GetUtcNow(),
            dbPath: first.DbPath);

        await restarted.Processor.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(300);
            Assert.Equal(0, restarted.Recorder.Ticks);      // durable state ⇒ not due
            Assert.Equal(1, await restarted.CountJobsAsync()); // still just the first enqueue
        }
        finally
        {
            await restarted.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Completed_jobs_are_purged_after_the_retention_period()
    {
        await using var h = new JobsHarness(o => o.RetentionPeriod = TimeSpan.FromHours(2));
        await h.Queue.EnqueueAsync(new RecordJob("done"));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().AnyAsync(j => j.ProcessedAt != null);
            });

            h.Clock.Advance(TimeSpan.FromHours(3)); // past retention (2h) and the 1h purge throttle
            await h.WaitUntilAsync(async () => await h.CountJobsAsync() == 0);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }
}
