using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>A job that parks until the test lets its tag go: the lever for the lane tests.</summary>
public sealed record LaneJob(string Tag) : IJob;

/// <summary>Per tag: when its job has started, and the latch that lets it finish.</summary>
public sealed class LaneGate
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _entered = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _release = new(StringComparer.Ordinal);

    public Task Entered(string tag) => Latch(_entered, tag).Task;

    public bool HasStarted(string tag) => Entered(tag).IsCompleted;

    public void Enter(string tag) => Latch(_entered, tag).TrySetResult();

    public Task Released(string tag) => Latch(_release, tag).Task;

    public void Release(string tag) => Latch(_release, tag).TrySetResult();

    private static TaskCompletionSource Latch(ConcurrentDictionary<string, TaskCompletionSource> latches, string tag) =>
        latches.GetOrAdd(tag, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed class LaneJobHandler(LaneGate lanes, Recorder recorder) : ICommandHandler<LaneJob>
{
    public async Task Handle(LaneJob command)
    {
        lanes.Enter(command.Tag);
        await lanes.Released(command.Tag).WaitAsync(Current.Cancellation).ConfigureAwait(false);
        recorder.Add(command.Tag);
    }
}

/// <summary>
/// Jobs of one lane run one at a time, in the order they are due; different lanes run side by side. A pump
/// that takes ten minutes to program holds up that pump's work and nobody else's.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobLaneTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Jobs_of_different_lanes_run_side_by_side()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new LaneJob("a"), TestContext.Current.CancellationToken).InLane("pump-a");
        await h.Queue.Enqueue(new LaneJob("b"), TestContext.Current.CancellationToken).InLane("pump-b");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            // Both have started and neither has finished: they run at the same time.
            await h.Lanes.Entered("a").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            await h.Lanes.Entered("b").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

            Assert.Empty(h.Recorder.Values);
        }
        finally
        {
            h.Lanes.Release("a");
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Jobs_of_one_lane_run_one_at_a_time_in_the_order_they_are_due()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new LaneJob("first"), TestContext.Current.CancellationToken).InLane("pump-a");
        await h.Queue.Enqueue(new LaneJob("second"), TestContext.Current.CancellationToken).InLane("pump-a");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Lanes.Entered("first").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
            await Task.Delay(Settle, TestContext.Current.CancellationToken); // many polls go by

            Assert.False(h.Lanes.HasStarted("second"), "the second job waits for the first");

            h.Lanes.Release("first");
            await h.Lanes.Entered("second").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);
        }
        finally
        {
            h.Lanes.Release("first");
            h.Lanes.Release("second");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_long_job_holds_up_its_own_lane_and_no_other()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new LaneJob("slow"), TestContext.Current.CancellationToken).InLane("pump-a");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Lanes.Entered("slow").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

            await h.Queue.Enqueue(new RecordJob("quick"), TestContext.Current.CancellationToken).InLane("pump-b");
            await h.Queue.Enqueue(new RecordJob("no-lane"), TestContext.Current.CancellationToken);

            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Values.Contains("quick") && h.Recorder.Values.Contains("no-lane")));
            Assert.DoesNotContain("slow", h.Recorder.Values);
        }
        finally
        {
            h.Lanes.Release("slow");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_lane_with_a_long_queue_does_not_crowd_the_others_out()
    {
        await using var h = new JobsHarness(o => o.BatchSize = 2);
        for (var i = 0; i < 6; i++)
        {
            await h.Queue.Enqueue(new RecordJob($"a{i}"), TestContext.Current.CancellationToken).InLane("pump-a");
        }

        await h.Queue.Enqueue(new RecordJob("b"), TestContext.Current.CancellationToken).InLane("pump-b");

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 7);

        Assert.Equal(["a0", "a1", "a2", "a3", "a4", "a5"], h.Recorder.Values.Where(v => v.StartsWith('a')));
        Assert.Contains("b", h.Recorder.Values);
    }

    [Fact]
    public async Task A_lane_cut_short_by_the_shutdown_is_given_back_without_counting_an_attempt()
    {
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.FromMilliseconds(50));
        await h.Queue.Enqueue(new LaneJob("cut"), TestContext.Current.CancellationToken).InLane("pump-a");
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Lanes.Entered("cut").WaitAsync(Rask.TestSupport.WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        await h.Processor.StopAsync(CancellationToken.None);

        var job = await h.SingleJobAsync();
        Assert.Null(job.ProcessedAt);
        Assert.Null(job.ClaimToken);
        Assert.Equal(0, job.Attempts);
    }

    [Fact]
    public async Task A_job_remembers_the_lane_it_was_enqueued_in()
    {
        await using var h = new JobsHarness();

        await h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken).InLane("pump-7");

        Assert.Equal("pump-7", (await h.SingleJobAsync()).Lane);
    }

    [Fact]
    public async Task A_database_without_the_lane_column_says_which_commands_to_run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nolane-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Job_Lane_ProcessedAt;", cancellationToken: TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Job DROP COLUMN Lane;", cancellationToken: TestContext.Current.CancellationToken);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.WaitUntilAsync(() => Task.FromResult(h.Logs.Any(l => l.Contains("rask db add AddJobLane", StringComparison.Ordinal))));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains(h.Logs, l => l.Contains("rask db update", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_fake_remembers_the_lane_a_job_was_enqueued_in()
    {
        using var jobs = Jobs.Fake();

        await Jobs.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken).InLane("pump-7");

        jobs.Enqueued<RecordJob>().InLane("pump-7").Once();
        jobs.Enqueued<RecordJob>().InLane("pump-8").None();
    }
}
