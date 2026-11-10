using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
/// A running job whose row is marked done or deleted from outside — a script, the dashboard's Delete — is stopped within one
/// look of the watch, found by its row id, and its row is left as the outsider left it.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobDoneElsewhereTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_running_job_whose_row_is_deleted_is_stopped_and_nothing_writes_the_row_back()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await using (var db = h.NewContext())
            {
                await db.Set<Job>().Where(j => j.Id == id).ExecuteDeleteAsync(Ct);
            }

            await h.Recorded("a:stopped:DoneElsewhere");
            await h.Logged($"Job {id} (Rask.Background.Tests.ParkJob, lane pump-1) was marked done or deleted outside the processor; stopped.");
            await h.WatchedAsync();

            Assert.Null(await h.Row(id));
            Assert.DoesNotContain(h.Logs, l => l.Contains("lost its lease", StringComparison.Ordinal));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_running_job_marked_done_from_outside_is_stopped_and_its_row_keeps_what_they_wrote()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");
            var theirs = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-42);

            await using (var db = h.NewContext())
            {
                await db.Set<Job>().Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ProcessedAt, theirs), Ct);
            }

            await h.Recorded("a:stopped:DoneElsewhere");
            await h.WatchedAsync();
            var row = await h.Row(id);

            Assert.Equal(theirs, row!.ProcessedAt);
            Assert.Null(row.Stopped);
            Assert.Null(row.Error);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_same_work_enqueued_again_is_never_taken_for_the_job_that_was_ended()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var first = await h.IdOf("a");

            // The identical job waits behind it — same lane, same payload: matched by payload it would look like the first.
            await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
            await using (var db = h.NewContext())
            {
                await db.Set<Job>().Where(j => j.Id == first).ExecuteDeleteAsync(Ct);
            }

            await h.Recorded("a:stopped:DoneElsewhere");
            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Values.Count(v => v == "a:stopped:DoneElsewhere") == 1));
            await h.WatchedAsync();
            h.Lanes.Release("a");
            await h.Recorded("a:done");

            Assert.Equal(["a:stopped:DoneElsewhere", "a:done"], h.Recorder.Values);
        }
        finally
        {
            h.Lanes.Release("a");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task While_a_job_ended_from_outside_still_runs_here_its_lane_waits_for_it()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new StubbornJob("a"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("b"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await using (var db = h.NewContext())
            {
                await db.Set<Job>().Where(j => j.Id == id).ExecuteDeleteAsync(Ct);
            }

            await h.WatchedAsync();
            await h.WatchedAsync();
            var bStartedWhileARan = h.Lanes.HasStarted("b");
            h.Lanes.Release("a");
            await h.Entered("b");

            Assert.False(bStartedWhileARan, "this host never runs two jobs of one lane at once");
            await h.Recorded("a:returned:DoneElsewhere");
            await h.Logged("was marked done or deleted outside the processor");
        }
        finally
        {
            h.Lanes.Release("a");
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_look_that_cannot_reach_the_database_stops_nothing_and_the_next_look_does()
    {
        await using var h = new JobsHarness(o => o.Limit<ParkJob>().After(TimeSpan.FromMinutes(1)));
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            h.Commands.Fail = _ => true;
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            await h.Logged("Could not check the running jobs; they run on unwatched until the next tick.");
            await h.WatchedAsync();
            var whileAway = h.Recorder.Values.ToList();
            h.Commands.Fail = null;
            var row = await h.Processed(id);

            Assert.Empty(whileAway);
            Assert.Equal(JobStop.TimeLimit, row.Stopped);
        }
        finally
        {
            h.Commands.Fail = null;
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }
}
