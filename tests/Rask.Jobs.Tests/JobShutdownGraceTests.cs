using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs.Tests;

/// <summary>
///     What happens to a job that is already running when the host is asked to stop. Before
///     <c>ShutdownGracePeriod</c>, the host's stopping token was passed straight into user code, so
///     <c>SIGTERM</c> cancelled a handler mid-call — a job halfway through a <c>SaveChangesAsync</c> was
///     simply torn in two and re-run whole on the next boot.
/// </summary>
public sealed class JobShutdownGraceTests
{
    [Fact]
    public async Task An_in_flight_job_finishes_within_the_grace()
    {
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.FromSeconds(5));
        await h.Queue.EnqueueAsync(new GateJob());

        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Stop while the handler is parked. The grace deadline is armed by this, not tripped.
        var stop = h.Processor.StopAsync(CancellationToken.None);
        h.Gate.Release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(h.Gate.Completed.Task.IsCompletedSuccessfully, "the handler ran to completion");
        var job = await h.SingleJobAsync();
        Assert.NotNull(job.ProcessedAt);
        // 1, not 0: the claim counts attempts *started*, so a job that succeeds first time shows one. The
        // roll-back on the shutdown path only applies to work that did NOT finish.
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Shutdown_still_refuses_to_start_the_next_job()
    {
        // The grace covers the job already running — it must not turn into "drain the whole batch",
        // which would make shutdown take one grace period per remaining job.
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.FromSeconds(5));
        await h.Queue.EnqueueAsync(new GateJob());
        await h.Queue.EnqueueAsync(new RecordJob("second"));

        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stop = h.Processor.StopAsync(CancellationToken.None);
        h.Gate.Release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        await using var db = h.NewContext();
        var jobs = await db.Set<Job>().OrderBy(j => j.Id).ToListAsync();
        Assert.NotNull(jobs[0].ProcessedAt);
        Assert.Null(jobs[1].ProcessedAt);
        Assert.Equal(0, jobs[1].Attempts);
        Assert.DoesNotContain("second", h.Recorder.Values);
    }

    [Fact]
    public async Task A_grace_expiry_does_not_count_a_failed_attempt()
    {
        // The decision this test exists to protect. MaxAttempts defaults to 25 here and 10 in Outbox/Mail;
        // counting a redeploy as an attempt would let deploy cadence alone march never-failing work to its
        // dead letter. The row must stay exactly as eligible as it was.
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.FromMilliseconds(50));
        await h.Queue.EnqueueAsync(new GateJob());
        var runAtBefore = (await h.SingleJobAsync()).RunAt;

        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Never released: the grace expires and the handler is cancelled.
        await h.Processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        var job = await h.SingleJobAsync();
        Assert.Null(job.ProcessedAt);
        // The claim increments Attempts up front, so the shutdown path has to give it back — otherwise
        // MaxAttempts becomes a function of deploy cadence rather than of failure.
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
        Assert.Equal(runAtBefore, job.RunAt);
        // The lease goes back too, so the next boot sees the job at once instead of waiting out a claim
        // held by a process that no longer exists.
        Assert.Null(job.ClaimToken);
        Assert.Null(job.ClaimedUntil);
    }

    [Fact]
    public async Task A_zero_grace_cancels_immediately()
    {
        // The documented opt-out, and the pre-existing behaviour.
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.Zero);
        await h.Queue.EnqueueAsync(new GateJob());

        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var started = Environment.TickCount64;
        await h.Processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(Environment.TickCount64 - started < 2_000, "a zero grace must not wait");
        Assert.False(h.Gate.Completed.Task.IsCompleted);
        Assert.Null((await h.SingleJobAsync()).ProcessedAt);
    }

    [Fact]
    public void The_grace_defaults_to_five_seconds()
    {
        // Sized to fit the deploy ladder: `docker stop -t 20` ⊃ HostOptions.ShutdownTimeout 15s ⊃ this.
        Assert.Equal(TimeSpan.FromSeconds(5), new JobOptions().ShutdownGracePeriod);
    }

    [Fact]
    public void A_negative_grace_is_rejected_at_registration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            new JobOptions { ShutdownGracePeriod = TimeSpan.FromSeconds(-1) }.Validate);
    }

    [Fact]
    public void A_grace_CancelAfter_cannot_take_is_rejected_at_registration()
    {
        // CancellationTokenSource.CancelAfter throws above int.MaxValue ms — and it would throw from the
        // shutdown path, the worst place to find out.
        Assert.Throws<ArgumentOutOfRangeException>(
            new JobOptions { ShutdownGracePeriod = TimeSpan.FromDays(30) }.Validate);
    }

    [Fact]
    public async Task A_handler_that_cancels_itself_still_counts_as_a_failure()
    {
        // Pins the catch filter. It tests the HOST token, not the grace token, precisely so a handler's own
        // OperationCanceledException stays an ordinary failure — the grace deadline is only ever armed by
        // the host token firing, so a real grace expiry always satisfies the filter anyway.
        await using var h = new JobsHarness();
        await h.Queue.EnqueueAsync(new SelfCancellingJob());

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().AnyAsync(j => j.Attempts > 0);
            });
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        var job = await h.SingleJobAsync();
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.ProcessedAt);
        Assert.Contains("gave up on its own", job.Error);
    }
}
