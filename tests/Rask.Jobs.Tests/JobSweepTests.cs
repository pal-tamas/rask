using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
/// F-27: a job whose host died on its last attempt is never claimed again, so nothing would ever say how it ended — the sweep
/// makes it the dead letter it is, instead of a job that reads as running for ever.
/// </summary>
public sealed class JobSweepTests
{
    private static JobsHarness Harness() => new(o =>
    {
        o.MaxAttempts = 2;
        o.LeaseDuration = TimeSpan.FromMinutes(1);
    });

    private static JobSweep<JobsDbContext> Sweep(JobsHarness h) =>
        new(h.Get<IDbContextFactory<JobsDbContext>>(), h.Clock, h.Get<JobsOptions>());

    // Claimed and never ended, as a host that died with it leaves it: once per attempt it is given.
    private static async Task CrashAsync(JobsHarness h, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            await using var db = h.NewContext();
            Assert.Single(await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, TestContext.Current.CancellationToken));
            h.Clock.Advance(TimeSpan.FromMinutes(2));
        }
    }

    [Fact]
    public async Task A_job_whose_host_died_on_its_last_attempt_ends_a_dead_letter_that_says_why()
    {
        await using var h = Harness();
        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken);
        await CrashAsync(h, attempts: 2);

        var swept = await Sweep(h).Sweep(TestContext.Current.CancellationToken);

        var job = await h.SingleJobAsync();
        Assert.Equal(job.Id, Assert.Single(swept).Id);
        Assert.Equal(JobSweep<JobsDbContext>.Abandoned, job.Error);
        Assert.Null(job.ClaimToken);
        Assert.Null(job.ProcessedAt);
        Assert.Equal(WorkState.Failed, JobWork.StateOf(job, maxAttempts: 2, h.Clock.GetUtcNow().UtcDateTime));
    }

    [Fact]
    public async Task A_job_with_an_attempt_left_is_claimed_again_not_swept()
    {
        await using var h = Harness();
        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken);
        await CrashAsync(h, attempts: 1);

        var swept = await Sweep(h).Sweep(TestContext.Current.CancellationToken);

        Assert.Empty(swept);
        Assert.Null((await h.SingleJobAsync()).Error);
    }

    [Fact]
    public async Task A_last_attempt_still_inside_its_lease_is_running_not_stuck()
    {
        await using var h = Harness();
        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken);
        await CrashAsync(h, attempts: 1);
        await using (var db = h.NewContext())
        {
            Assert.Single(await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, TestContext.Current.CancellationToken));
        }

        var swept = await Sweep(h).Sweep(TestContext.Current.CancellationToken);

        Assert.Empty(swept);
        Assert.NotNull((await h.SingleJobAsync()).ClaimToken);
    }

    [Fact]
    public async Task A_second_sweep_finds_nothing_left_to_end()
    {
        await using var h = Harness();
        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken);
        await CrashAsync(h, attempts: 2);
        await Sweep(h).Sweep(TestContext.Current.CancellationToken);

        var again = await Sweep(h).Sweep(TestContext.Current.CancellationToken);

        Assert.Empty(again);
    }

    [Fact]
    public async Task The_processor_sweeps_by_itself_and_logs_the_dead_letter()
    {
        await using var h = Harness();
        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken);
        await CrashAsync(h, attempts: 2);

        await h.RunUntilAsync(() => h.Logs.Any(line => line.Contains("is a dead letter", StringComparison.Ordinal)));

        Assert.Equal(JobSweep<JobsDbContext>.Abandoned, (await h.SingleJobAsync()).Error);
        Assert.Empty(h.Recorder.Values);
    }
}
