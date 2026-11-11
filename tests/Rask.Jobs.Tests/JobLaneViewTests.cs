using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Background.Tests;

/// <summary>
/// <c>Jobs.Lanes(…)</c>: what runs in each lane and what waits, in the order the lane takes it — many lanes in one query.
/// Rows are written here as the processor and outsiders would leave them; no processor runs.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobLaneViewTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_lanes_asked_for_answers_nothing_without_a_query()
    {
        await using var h = new JobsHarness();
        var before = h.Commands.Reads;

        var lanes = await h.Queue.Lanes([], Ct);

        Assert.Empty(lanes);
        Assert.Equal(before, h.Commands.Reads);
    }

    [Fact]
    public async Task Many_lanes_are_read_in_one_query_and_every_lane_asked_for_is_answered()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a1"), Ct).InLane("pump-a");
        await h.Queue.Enqueue(new ParkJob("b1"), Ct).InLane("pump-b");
        await h.Queue.Enqueue(new ParkJob("c1"), Ct).InLane("pump-c");
        await h.Queue.Enqueue(new ParkJob("elsewhere"), Ct).InLane("pump-z");
        var before = h.Commands.Reads;

        var lanes = await h.Queue.Lanes(["pump-a", "pump-b", "pump-c", "pump-idle", "pump-a"], Ct);

        Assert.Equal(before + 1, h.Commands.Reads);
        Assert.Equal(["pump-a", "pump-b", "pump-c", "pump-idle"], lanes.Keys.Order(StringComparer.Ordinal));
        Assert.True(lanes["pump-idle"].IsIdle);
        Assert.Equal("pump-idle", lanes["pump-idle"].Name);
        Assert.Equal(new ParkJob("b1"), Assert.Single(lanes["pump-b"].Waiting).Job);
    }

    [Fact]
    public async Task The_claimed_job_runs_and_the_rest_wait_in_the_order_the_lane_takes_them()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("later"), Ct).InLane("pump-1").In(TimeSpan.FromMinutes(2));
        await h.Queue.Enqueue(new ParkJob("running"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("next"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("after-next"), Ct).InLane("pump-1");
        var running = await h.IdOf("running");
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await Update(h, running, s => s
            .SetProperty(j => j.ClaimToken, Guid.NewGuid())
            .SetProperty(j => j.ClaimedUntil, now.AddMinutes(5))
            .SetProperty(j => j.StartedAt, now)
            .SetProperty(j => j.Attempts, 1));

        var lane = await h.Queue.Lanes(["pump-1"], Ct);

        var pump = lane["pump-1"];
        Assert.Equal(running, pump.Running!.Id);
        Assert.Equal(new ParkJob("running"), pump.Running.Job);
        Assert.Equal("Rask.Background.Tests.ParkJob", pump.Running.Type);
        Assert.Equal(now, pump.Running.Started);
        Assert.Equal(1, pump.Running.Attempts);
        Assert.False(pump.Running.Stopping);
        Assert.Equal(["next", "after-next", "later"], pump.Waiting.Select(w => ((ParkJob)w.Job!).Tag));
        Assert.Equal(now.AddMinutes(2), pump.Waiting[2].Due);
        Assert.False(pump.IsIdle);
    }

    [Fact]
    public async Task A_job_waiting_out_a_retry_counts_as_waiting_with_its_due_ahead()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("retry"), Ct).InLane("pump-1");
        var id = await h.IdOf("retry");
        var due = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(40);
        await Update(h, id, s => s.SetProperty(j => j.Attempts, 2).SetProperty(j => j.RunAt, due).SetProperty(j => j.Error, "boom"));

        var lane = (await h.Queue.Lanes(["pump-1"], Ct))["pump-1"];

        Assert.Null(lane.Running);
        var waiting = Assert.Single(lane.Waiting);
        Assert.Equal(due, waiting.Due);
        Assert.Equal(2, waiting.Attempts);
    }

    [Fact]
    public async Task Dead_letters_processed_and_stopped_jobs_are_left_out_but_a_last_attempt_still_running_is_not()
    {
        await using var h = new JobsHarness(o => o.MaxAttempts = 3);
        await h.Queue.Enqueue(new ParkJob("dead"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("done"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("stopped"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("last-try"), Ct).InLane("pump-2");
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await Update(h, await h.IdOf("dead"), s => s.SetProperty(j => j.Attempts, 3));
        await Update(h, await h.IdOf("done"), s => s.SetProperty(j => j.ProcessedAt, now));
        await Update(h, await h.IdOf("stopped"), s => s.SetProperty(j => j.ProcessedAt, now).SetProperty(j => j.Stopped, JobStop.Asked));
        await Update(h, await h.IdOf("last-try"), s => s.SetProperty(j => j.Attempts, 3).SetProperty(j => j.ClaimedUntil, now.AddMinutes(5)));

        var lanes = await h.Queue.Lanes(["pump-1", "pump-2"], Ct);

        Assert.True(lanes["pump-1"].IsIdle);
        Assert.Equal(new ParkJob("last-try"), lanes["pump-2"].Running!.Job);
    }

    [Fact]
    public async Task A_running_job_asked_to_stop_is_still_running_and_says_it_is_stopping()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        var id = await h.IdOf("a");
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await Update(h, id, s => s.SetProperty(j => j.ClaimedUntil, now.AddMinutes(5)).SetProperty(j => j.Stopped, JobStop.Asked));

        var lane = (await h.Queue.Lanes(["pump-1"], Ct))["pump-1"];

        Assert.Equal(id, lane.Running!.Id);
        Assert.True(lane.Running.Stopping);
    }

    [Fact]
    public async Task A_payload_that_no_longer_reads_is_still_listed_with_its_type()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("broken"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("renamed"), Ct).InLane("pump-1");
        await Update(h, await h.IdOf("broken"), s => s.SetProperty(j => j.Payload, "{not json"));
        await Update(h, await h.IdOf("renamed"), s => s.SetProperty(j => j.Type, "Gone.Away.OldJob"));

        var lane = (await h.Queue.Lanes(["pump-1"], Ct))["pump-1"];

        Assert.Equal(2, lane.Waiting.Count);
        Assert.All(lane.Waiting, w => Assert.Null(w.Job));
        Assert.Equal(["Rask.Background.Tests.ParkJob", "Gone.Away.OldJob"], lane.Waiting.Select(w => w.Type));
    }

    [Fact]
    public async Task A_claimed_job_whose_work_has_ended_here_is_no_longer_running()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        var id = await h.IdOf("a");
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await Update(h, id, s => s.SetProperty(j => j.ClaimedUntil, now.AddMinutes(5)));
        await using (var db = h.NewContext())
        {
            var job = await db.Set<Job>().SingleAsync(j => j.Id == id, Ct);
            var run = new JobRun(job, h.Clock, CancellationToken.None);
            run.End();
            h.Get<JobsOptions>().Signals.Began(run);
        }

        var lane = (await h.Queue.Lanes(["pump-1"], Ct))["pump-1"];

        Assert.True(lane.IsIdle);
    }

    [Fact]
    public async Task The_fake_queue_answers_every_lane_asked_for_as_idle_and_stops_nothing()
    {
        using var jobs = Jobs.Fake();
        IJobs fake = jobs;

        var lanes = await fake.Lanes(["a", "b"], Ct);
        var stopped = await fake.Stop(1, Ct);

        Assert.True(lanes["a"].IsIdle);
        Assert.True(lanes["b"].IsIdle);
        Assert.False(stopped);
    }

    private static async Task Update(JobsHarness h, long id, Action<UpdateSettersBuilder<Job>> set)
    {
        await using var db = h.NewContext();
        await db.Set<Job>().Where(j => j.Id == id).ExecuteUpdateAsync(set, Ct);
    }
}
