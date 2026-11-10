using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
/// <c>Jobs.Stop(id)</c>: a running job is cancelled and ends stopped, a waiting one never runs, and a stop is never a failure.
/// The behaviours TA proved on its pumps, kept here on a fake clock.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobStopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stopping_a_running_job_cancels_its_work_and_ends_it_stopped_not_failed()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            var stopped = await h.Queue.Stop(id, Ct);

            Assert.True(stopped);
            await h.Recorded("a:stopped:Asked");
            var row = await h.Processed(id);
            Assert.Equal(JobStop.Asked, row.Stopped);
            Assert.Null(row.Error);
            Assert.Null(row.ClaimToken);
            Assert.Null(row.ClaimedUntil);
            Assert.Equal(1, row.Attempts);
            await h.Logged($"Job {id} (Rask.Background.Tests.ParkJob, lane pump-1) was stopped on request.");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_stopped_job_is_not_tried_again_and_never_becomes_a_dead_letter()
    {
        await using var h = new JobsHarness(o =>
        {
            o.MaxAttempts = 1;
            o.BaseRetryDelay = TimeSpan.Zero;
            o.MaxRetryDelay = TimeSpan.Zero;
        });
        await h.Queue.Enqueue(new ParkJob("a"), Ct);
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await h.Queue.Stop(id, Ct);
            await h.Processed(id);
            await h.WatchedAsync();

            var row = await h.Row(id);
            Assert.Equal(JobStop.Asked, row!.Stopped);
            Assert.NotNull(row.ProcessedAt);
            Assert.Null(row.Error);
            Assert.Equal(["a:stopped:Asked"], h.Recorder.Values);
            Assert.DoesNotContain(h.Logs, l => l.Contains("failed (attempt", StringComparison.Ordinal));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Whatever_the_work_throws_after_a_stop_counts_as_the_stop()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new GrumbleJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await h.Queue.Stop(id, Ct);
            var row = await h.Processed(id);

            Assert.Equal(JobStop.Asked, row.Stopped);
            Assert.Null(row.Error);
            Assert.DoesNotContain(h.Logs, l => l.Contains("failed (attempt", StringComparison.Ordinal));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Once_a_stop_is_asked_the_job_ends_stopped_whatever_its_handler_returns()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var awaited = h.Queue.Run(new ShrugJob("a"), Ct).InLane("pump-1").AsTask();
            await h.Entered("a");
            var id = await h.IdOf("a");

            await h.Queue.Stop(id, Ct);
            var thrown = await Assert.ThrowsAsync<JobStoppedException>(() => awaited);
            var row = await h.Processed(id);

            Assert.Equal(JobStop.Asked, thrown.Stop);
            Assert.Equal(JobStop.Asked, row.Stopped);
            Assert.Null(row.Result);
            Assert.Contains("a:shrugged", h.Recorder.Values);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task An_awaited_job_that_is_stopped_throws_a_cancellation_that_says_why()
    {
        await using var h = new JobsHarness();
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var awaited = h.Queue.Run(new AtPump("a"), Ct).InLane("pump-1").AsTask();
            await h.Entered("a");
            var id = await h.IdOf("a");

            await h.Queue.Stop(id, Ct);
            var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => awaited);

            var stop = Assert.IsType<JobStoppedException>(thrown);
            Assert.Equal(JobStop.Asked, stop.Stop);
            Assert.Equal("Rask.Background.Tests.AtPump", stop.JobType);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_first_reason_stands_and_a_second_stop_or_a_later_limit_changes_nothing()
    {
        await using var h = new JobsHarness(o => o.Limit<StubbornJob>().After(TimeSpan.FromMinutes(10)));
        await h.Queue.Enqueue(new StubbornJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            var first = await h.Queue.Stop(id, Ct);
            var second = await h.Queue.Stop(id, Ct);
            h.Clock.Advance(TimeSpan.FromMinutes(10));
            await h.WatchedAsync();
            h.Lanes.Release("a");
            var row = await h.Processed(id);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal(JobStop.Asked, row.Stopped);
            Assert.Contains("a:returned:Asked", h.Recorder.Values);
            Assert.DoesNotContain(h.Logs, l => l.Contains("time limit", StringComparison.Ordinal));
        }
        finally
        {
            h.Lanes.Release("a");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stopping_a_job_that_is_done_or_does_not_exist_answers_false()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("done"), Ct);
        await h.RunUntilAsync(() => h.Recorder.Values.Contains("done"));
        var id = await h.IdOf("done");
        await h.Processed(id);

        var done = await h.Queue.Stop(id, Ct);
        var missing = await h.Queue.Stop(id + 1000, Ct);

        Assert.False(done);
        Assert.False(missing);
        Assert.Null((await h.Row(id))!.Stopped);
    }

    [Fact]
    public async Task Stopping_a_waiting_job_ends_it_at_once_and_leaves_the_running_one_alone()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("b"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var running = await h.IdOf("a");
            var waiting = await h.IdOf("b");

            var stopped = await h.Queue.Stop(waiting, Ct);
            var b = await h.Row(waiting);
            await h.WatchedAsync();
            var a = await h.Row(running);
            h.Lanes.Release("a");
            await h.Recorded("a:done");
            await h.Processed(running);
            await h.WatchedAsync();

            Assert.True(stopped);
            Assert.Equal(JobStop.Asked, b!.Stopped);
            Assert.NotNull(b.ProcessedAt);
            Assert.Equal(0, b.Attempts);
            Assert.Null(a!.Stopped);
            Assert.Null(a.ProcessedAt);
            Assert.False(h.Lanes.HasStarted("b"));
            Assert.Null((await h.Row(running))!.Stopped);
        }
        finally
        {
            h.Lanes.Release("a");
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_stop_by_id_never_reaches_a_different_job()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("b"), Ct).InLane("pump-2");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            await h.Entered("b");
            var a = await h.IdOf("a");
            var b = await h.IdOf("b");

            await h.Queue.Stop(a, Ct);
            await h.Recorded("a:stopped:Asked");
            await h.WatchedAsync();
            h.Lanes.Release("b");
            await h.Recorded("b:done");
            var other = await h.Processed(b);

            Assert.Null(other.Stopped);
            Assert.DoesNotContain("b:stopped:Asked", h.Recorder.Values);
        }
        finally
        {
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_asked_to_stop_before_its_host_took_it_never_runs()
    {
        // A stop that lands on a claimed row whose host then dies: the row keeps the request, and whoever claims it next ends it.
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        var id = await h.IdOf("a");
        await using (var db = h.NewContext())
        {
            var past = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
            await db.Set<Job>().Where(j => j.Id == id).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Stopped, JobStop.Asked).SetProperty(j => j.ClaimedUntil, past), Ct);
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            var row = await h.Processed(id);

            Assert.Equal(JobStop.Asked, row.Stopped);
            Assert.False(h.Lanes.HasStarted("a"));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Shutdown_is_not_a_stop_and_the_job_runs_again_on_the_next_boot()
    {
        await using var h = new JobsHarness(o => o.ShutdownGracePeriod = TimeSpan.Zero);
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        await h.Entered("a");
        var id = await h.IdOf("a");

        await h.Processor.StopAsync(CancellationToken.None);
        var row = await h.Row(id);

        Assert.Contains("a:cancelled:none", h.Recorder.Values);
        Assert.Null(row!.ProcessedAt);
        Assert.Null(row.Stopped);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.ClaimToken);
        Assert.Null(row.ClaimedUntil);
    }

    [Fact]
    public async Task Work_a_job_saved_before_its_stop_stays_saved()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new SavingJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await h.Queue.Stop(id, Ct);
            await h.Processed(id);
            await using var db = h.NewContext();
            var saved = await db.Set<Job>().AsNoTracking().CountAsync(j => j.Payload.Contains("a:saved"), Ct);

            Assert.Equal(1, saved);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_handler_that_ignores_its_stop_holds_its_lane_and_is_named_once_a_minute()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new StubbornJob("a"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("b"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");
            await h.Queue.Stop(id, Ct);
            const string ignores = "after it was stopped; its work does not pass Current.Cancellation on";

            h.Clock.Advance(TimeSpan.FromSeconds(59));
            await h.WatchedAsync();
            var beforeAMinute = h.LogCount(ignores);
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            await h.Logged(ignores);
            h.Clock.Advance(TimeSpan.FromSeconds(30));
            await h.WatchedAsync();
            var halfAMinuteLater = h.LogCount(ignores);
            var lane = await h.Queue.Lanes(["pump-1"], Ct);
            h.Clock.Advance(TimeSpan.FromSeconds(30));
            await h.WatchedAsync();
            var aMinuteLater = h.LogCount(ignores);
            var bStartedWhileAHeld = h.Lanes.HasStarted("b");
            h.Lanes.Release("a");
            await h.Entered("b");
            var row = await h.Row(id);

            Assert.Equal(0, beforeAMinute);
            Assert.Equal(1, halfAMinuteLater);
            Assert.Equal(2, aMinuteLater);
            Assert.False(bStartedWhileAHeld, "a job that ignores its stop holds its lane");
            Assert.Equal(id, lane["pump-1"].Running!.Id);
            Assert.True(lane["pump-1"].Running!.Stopping);
            Assert.Contains("a:returned:Asked", h.Recorder.Values);
            Assert.Equal(JobStop.Asked, row!.Stopped);
        }
        finally
        {
            h.Lanes.Release("a");
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("Stopped")]
    [InlineData("StartedAt")]
    public async Task A_database_without_the_stop_columns_says_which_commands_to_run(string column)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nostop-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await h.Queue.Enqueue(new RecordJob("waiting"), Ct);
        await using (var db = h.NewContext())
        {
#pragma warning disable EF1002 // The column is one of two constants above, not input.
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE Job DROP COLUMN {column};", cancellationToken: Ct);
#pragma warning restore EF1002
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Logged("rask db add AddJobStop && rask db update");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(h.Logs, l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Outside_a_job_nothing_is_stopped_and_a_report_does_nothing()
    {
        var before = Jobs.Stopped;

        Jobs.Report("nobody listens");

        Assert.Null(before);
        Assert.Null(Jobs.Stopped);
    }
}
