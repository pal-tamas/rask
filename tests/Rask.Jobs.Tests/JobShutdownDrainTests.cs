using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
///     A deploy SIGTERMs the old container while a job may be halfway through talking to real hardware. The old
///     container claims nothing new from then on, but the job it is running keeps a live token until the host's
///     own shutdown budget runs out — the one `rask deploy` sizes from `stopTimeout`.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobShutdownDrainTests
{
    [Fact]
    public void A_running_job_is_not_cut_by_a_grace_of_its_own_by_default()
    {
        var options = new JobsOptions();

        var grace = options.ShutdownGracePeriod;

        Assert.Equal(Timeout.InfiniteTimeSpan, grace);
    }

    [Fact]
    public void The_infinite_grace_passes_validation()
    {
        var options = new JobsOptions { ShutdownGracePeriod = Timeout.InfiniteTimeSpan };

        var result = new JobsOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public async Task An_in_flight_job_outlives_the_stop_signal_until_it_is_done()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new GateJob(), TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        var stop = h.Processor.StopAsync(CancellationToken.None);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        var stoppedEarly = stop.IsCompleted;
        var cutEarly = h.Gate.Completed.Task.IsCompleted;
        h.Gate.Release.SetResult();
        await stop.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.False(stoppedEarly, "stopping waits for the running job");
        Assert.False(cutEarly, "the job was still at work when it was released");
        Assert.True(h.Gate.Completed.Task.IsCompletedSuccessfully, "the handler ran to completion");
        Assert.NotNull((await h.SingleJobAsync()).ProcessedAt);
    }

    [Fact]
    public async Task The_end_of_the_host_budget_cancels_the_running_job()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new GateJob(), TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await h.Processor.StopAsync(budget.Token).WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.False(h.Gate.Completed.Task.IsCompleted);
        var job = await h.SingleJobAsync();
        Assert.Null(job.ProcessedAt);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.ClaimToken);
    }

    [Fact]
    public async Task Nothing_is_claimed_after_the_stop_signal()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new GateJob(), TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        var stop = h.Processor.StopAsync(CancellationToken.None);
        await h.Queue.Enqueue(new RecordJob("late"), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        h.Gate.Release.SetResult();
        await stop.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        await using var db = h.NewContext();
        var late = await db.Set<Job>().OrderBy(j => j.Id).LastAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, late.Attempts);
        Assert.Null(late.ClaimToken);
        Assert.DoesNotContain("late", h.Recorder.Values);
    }

    [Fact]
    public async Task While_stopping_it_says_which_jobs_it_waits_for()
    {
        await using var h = new JobsHarness();
        h.Jobs.DrainReportInterval = TimeSpan.FromMilliseconds(20);
        await h.Queue.Enqueue(new GateJob(), TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        var stop = h.Processor.StopAsync(CancellationToken.None);
        await h.WaitUntilAsync(() => Task.FromResult(h.Logs.Count(l => l.Contains("waiting for 1 running job(s) to finish: GateJob (", StringComparison.Ordinal)) >= 2));
        h.Gate.Release.SetResult();
        await stop.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Contains(h.Logs, l => l.StartsWith("Stopping: no new job is claimed; waiting for 1 running job(s) to finish: GateJob (", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_job_longer_than_its_lease_keeps_the_lease_while_it_runs()
    {
        await using var h = new JobsHarness(o => o.LeaseDuration = TimeSpan.FromMilliseconds(300));
        await h.Queue.Enqueue(new GateJob(), TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Gate.Entered.Task.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        var firstLease = (await h.SingleJobAsync()).ClaimedUntil!.Value;

        for (var step = 0; step < 4; step++)
        {
            h.Clock.Advance(TimeSpan.FromMilliseconds(150));
            var now = h.Clock.GetUtcNow().UtcDateTime;
            await h.WaitUntilAsync(async () => (await h.SingleJobAsync()).ClaimedUntil >= now.AddMilliseconds(300));
        }

        var renewed = (await h.SingleJobAsync()).ClaimedUntil!.Value;
        h.Gate.Release.SetResult();
        await h.WaitUntilAsync(async () => (await h.SingleJobAsync()).ProcessedAt is not null);
        await h.Processor.StopAsync(CancellationToken.None);

        var job = await h.SingleJobAsync();
        Assert.True(renewed > firstLease.AddMilliseconds(500), "the lease moved on with the clock");
        Assert.Equal(1, job.Attempts);
        Assert.Null(job.ClaimToken);
    }
}
