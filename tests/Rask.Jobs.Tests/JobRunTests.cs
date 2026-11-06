using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>A job a caller awaits that parks until the test lets its tag go, then answers which tag it was.</summary>
public sealed record AtPump(string Tag) : IJob<string>;

public sealed class AtPumpHandler(LaneGate lanes, Recorder recorder) : IJobHandler<AtPump, string>
{
    public async Task<string> Handle(AtPump job)
    {
        lanes.Enter(job.Tag);
        await lanes.Released(job.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
        recorder.Add(job.Tag);
        return $"done:{job.Tag}";
    }
}

public sealed record PumpTotal(int Litres, string Pump);

/// <summary>A job that answers a record at once.</summary>
public sealed record ReadTotal(string Pump, int A, int B) : IJob<PumpTotal>;

public sealed class ReadTotalHandler : IJobHandler<ReadTotal, PumpTotal>
{
    public Task<PumpTotal> Handle(ReadTotal job) => Task.FromResult(new PumpTotal(job.A + job.B, job.Pump));
}

/// <summary>A job a caller awaits whose handler always throws.</summary>
public sealed record UnreachablePump : IJob<int>;

public sealed class UnreachablePumpHandler : IJobHandler<UnreachablePump, int>
{
    public Task<int> Handle(UnreachablePump job) => throw new InvalidOperationException("pump 7 did not answer");
}

/// <summary>A job that answers nothing, awaited all the same.</summary>
public sealed record Calibrate(string Pump) : IJob;

public sealed class CalibrateHandler(Recorder recorder) : IJobHandler<Calibrate>
{
    public Task Handle(Calibrate job)
    {
        recorder.Add($"calibrated:{job.Pump}");
        return Task.CompletedTask;
    }
}

/// <summary>A job of lane "self" that tries to await another job of lane "self", and records what it was told.</summary>
public sealed record AwaitOwnLane : IJob;

public sealed class AwaitOwnLaneHandler(Recorder recorder) : IJobHandler<AwaitOwnLane>
{
    public async Task Handle(AwaitOwnLane job)
    {
        try
        {
            await Jobs.Run(new ReadTotal("self", 1, 1)).InLane("self");
            recorder.Add("awaited");
        }
        catch (InvalidOperationException ex)
        {
            recorder.Add(ex.Message);
        }
    }
}

/// <summary>
/// <c>await Jobs.Run(job).InLane(key)</c>: the job is stored and run like any lane job - one at a time with that lane's
/// other jobs - and the caller awaits its answer or its failure. Giving up the wait never gives up the job.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobRunTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Two_awaited_jobs_of_one_lane_run_one_after_the_other()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var first = h.Queue.Run(new AtPump("first"), TestContext.Current.CancellationToken).InLane("pump-7").AsTask();
            await h.Lanes.Entered("first").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            var second = h.Queue.Run(new AtPump("second"), TestContext.Current.CancellationToken).InLane("pump-7").AsTask();

            await Task.Delay(Settle, TestContext.Current.CancellationToken); // many polls go by
            var secondStartedEarly = h.Lanes.HasStarted("second");
            h.Lanes.Release("first");
            var firstAnswer = await first.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            await h.Lanes.Entered("second").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            h.Lanes.Release("second");
            var secondAnswer = await second.WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

            Assert.False(secondStartedEarly, "the second job of the lane waits for the first");
            Assert.Equal("done:first", firstAnswer);
            Assert.Equal("done:second", secondAnswer);
            Assert.Equal(["first", "second"], h.Recorder.Values);
        }
        finally
        {
            h.Lanes.Release("first");
            h.Lanes.Release("second");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Awaited_jobs_of_different_lanes_run_side_by_side()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var a = h.Queue.Run(new AtPump("a"), TestContext.Current.CancellationToken).InLane("pump-a").AsTask();
            var b = h.Queue.Run(new AtPump("b"), TestContext.Current.CancellationToken).InLane("pump-b").AsTask();

            await h.Lanes.Entered("a").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            await h.Lanes.Entered("b").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            var finishedBeforeRelease = h.Recorder.Values.Count;
            h.Lanes.Release("a");
            h.Lanes.Release("b");

            Assert.Equal(0, finishedBeforeRelease);
            Assert.Equal(["done:a", "done:b"], await Task.WhenAll(a, b).WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken));
        }
        finally
        {
            h.Lanes.Release("a");
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_answer_comes_back_as_the_type_the_job_declares()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            PumpTotal total = await h.Queue.Run(new ReadTotal("pump-7", 40, 2), TestContext.Current.CancellationToken).InLane("pump-7");

            Assert.Equal(new PumpTotal(42, "pump-7"), total);
            var row = await h.SingleJobAsync();
            Assert.True(row.Awaited);
            Assert.NotNull(row.ProcessedAt);
            Assert.Contains("42", row.Result, StringComparison.Ordinal);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_that_answers_nothing_is_awaited_until_it_has_run()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Queue.Run(new Calibrate("pump-7"), TestContext.Current.CancellationToken).InLane("pump-7");

            Assert.Equal(["calibrated:pump-7"], h.Recorder.Values);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_failing_job_throws_its_error_at_the_caller_and_is_not_retried()
    {
        await using var h = new JobsHarness(o => o.BaseRetryDelay = TimeSpan.Zero);
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var run = () => h.Queue.Run(new UnreachablePump(), TestContext.Current.CancellationToken).InLane("pump-7").AsTask();

            var error = await Assert.ThrowsAsync<JobFailedException>(run);
            await Task.Delay(Settle, TestContext.Current.CancellationToken); // a retry would have been claimed by now

            Assert.Equal("pump 7 did not answer", error.Message);
            Assert.EndsWith(nameof(UnreachablePump), error.JobType, StringComparison.Ordinal);
            var row = await h.SingleJobAsync();
            Assert.Null(row.ProcessedAt);
            Assert.Equal(new JobsOptions().MaxAttempts, row.Attempts);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Cancelling_the_wait_leaves_the_job_running_to_the_end()
    {
        await using var h = new JobsHarness();
        using var gaveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var waiting = h.Queue.Run(new AtPump("cancelled"), gaveUp.Token).InLane("pump-7").AsTask();
            await h.Lanes.Entered("cancelled").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

            await gaveUp.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            h.Lanes.Release("cancelled");

            await h.WaitUntilAsync(async () => (await h.SingleJobAsync()).ProcessedAt is not null);
            Assert.Equal(["cancelled"], h.Recorder.Values);
            Assert.Equal("\"done:cancelled\"", (await h.SingleJobAsync()).Result);
        }
        finally
        {
            h.Lanes.Release("cancelled");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_wait_that_runs_out_throws_a_timeout_and_leaves_the_job_running()
    {
        await using var h = new JobsHarness();
        h.Clock.HoldTimers();
        var wait = TimeSpan.FromMilliseconds(200);
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var waiting = h.Queue.Run(new AtPump("slow"), TestContext.Current.CancellationToken).InLane("pump-7").Within(wait).AsTask();
            await h.Lanes.Entered("slow").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            await h.WaitUntilAsync(() => Task.FromResult(h.Clock.PendingTimers > 0));

            h.Clock.Advance(wait);
            var error = await Assert.ThrowsAsync<TimeoutException>(() => waiting);
            var stillRunning = h.Lanes.HasStarted("slow") && h.Recorder.Values.Count == 0;
            h.Lanes.Release("slow");

            Assert.Contains("still runs", error.Message, StringComparison.Ordinal);
            Assert.True(stillRunning);
            await h.WaitUntilAsync(async () => (await h.SingleJobAsync()).ProcessedAt is not null);
        }
        finally
        {
            h.Lanes.Release("slow");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_that_awaits_a_job_of_its_own_lane_is_refused_rather_than_waiting_forever()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new AwaitOwnLane(), TestContext.Current.CancellationToken).InLane("self");

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Contains("cannot await another job of lane 'self'", h.Recorder.Values[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_inside_Db_Transaction_is_refused_because_the_job_could_never_be_seen()
    {
        await using var h = new JobsHarness();
        Db.Configure(h.Get<IDbContextFactory<JobsDbContext>>());
        try
        {
            var run = () => Db.Transaction(
                async () => await h.Queue.Run(new Calibrate("pump-7"), TestContext.Current.CancellationToken).InLane("pump-7"),
                cancellationToken: TestContext.Current.CancellationToken);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(run);

            Assert.Contains("Db.Transaction", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, await h.CountJobsAsync());
        }
        finally
        {
            Db.Reset();
        }
    }

    [Fact]
    public async Task A_database_without_the_result_columns_says_which_commands_to_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-noresult-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await h.Queue.Enqueue(new RecordJob("waiting"), TestContext.Current.CancellationToken);
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Job DROP COLUMN Result;", cancellationToken: TestContext.Current.CancellationToken);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(h.Logs.Any(l => l.Contains("rask db add AddJobResult", StringComparison.Ordinal))));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains(h.Logs, l => l.Contains("rask db update", StringComparison.Ordinal));
    }
}
