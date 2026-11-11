using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
/// <c>c.Jobs.Limit&lt;TJob&gt;().After(…).IdleFor(…)</c>: a running job stops by itself at its time limit, or when it has
/// reported no progress for too long — measured exactly, on the processor's clock, which these tests move by hand.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobLimitTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_job_is_not_cut_one_second_short_of_its_idle_limit_and_is_cut_exactly_at_it()
    {
        await using var h = new JobsHarness(o => o.Limit<ChattyJob>().IdleFor(TimeSpan.FromMinutes(5)));
        await h.Queue.Enqueue(new ChattyJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            h.Clock.Advance(TimeSpan.FromMinutes(5) - Second);
            await h.WatchedAsync();
            var oneSecondShort = await h.Row(id);
            h.Clock.Advance(Second);
            var row = await h.Processed(id);

            Assert.Null(oneSecondShort!.Stopped);
            Assert.Null(oneSecondShort.ProcessedAt);
            Assert.Equal(JobStop.Idle, row.Stopped);
            await h.Recorded("a:stopped:Idle");
            await h.Logged($"Job {id} (Rask.Background.Tests.ChattyJob, lane pump-1) reported no progress for 00:05:00 and was stopped");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Time_waiting_in_the_lane_before_the_handler_starts_never_counts_as_idle()
    {
        await using var h = new JobsHarness(o => o.Limit<ChattyJob>().IdleFor(TimeSpan.FromMinutes(5)));
        await h.Queue.Enqueue(new ParkJob("first"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ChattyJob("second"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("first");
            var id = await h.IdOf("second");

            h.Clock.Advance(TimeSpan.FromHours(1));
            await h.WatchedAsync();
            h.Lanes.Release("first");
            await h.Entered("second");
            h.Clock.Advance(TimeSpan.FromMinutes(5) - Second);
            await h.WatchedAsync();
            var oneSecondShort = await h.Row(id);
            h.Clock.Advance(Second);
            var row = await h.Processed(id);

            Assert.Null(oneSecondShort!.ProcessedAt);
            Assert.Equal(JobStop.Idle, row.Stopped);
        }
        finally
        {
            h.Lanes.Release("first");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_job_that_reports_more_often_than_its_idle_limit_is_never_cut()
    {
        await using var h = new JobsHarness(o => o.Limit<ChattyJob>().IdleFor(TimeSpan.FromMinutes(5)));
        await h.Queue.Enqueue(new ChattyJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            for (var i = 0; i < 10; i++)
            {
                h.Clock.Advance(TimeSpan.FromMinutes(4));
                await h.WatchedAsync();
                await h.Pulses.Report("a");
            }

            var afterFortyMinutes = await h.Row(id);
            h.Clock.Advance(TimeSpan.FromMinutes(5));
            var row = await h.Processed(id);

            Assert.Null(afterFortyMinutes!.ProcessedAt);
            Assert.Equal(JobStop.Idle, row.Stopped);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task The_time_limit_cuts_a_job_exactly_at_it_however_much_progress_it_reports()
    {
        await using var h = new JobsHarness(o => o.Limit<ChattyJob>().After(TimeSpan.FromMinutes(10)).IdleFor(TimeSpan.FromMinutes(5)));
        await h.Queue.Enqueue(new ChattyJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            h.Clock.Advance(TimeSpan.FromMinutes(4));
            await h.Pulses.Report("a");
            h.Clock.Advance(TimeSpan.FromMinutes(4));
            await h.Pulses.Report("a");
            h.Clock.Advance(TimeSpan.FromMinutes(2) - Second);
            await h.WatchedAsync();
            var oneSecondShort = await h.Row(id);
            h.Clock.Advance(Second);
            var row = await h.Processed(id);

            Assert.Null(oneSecondShort!.ProcessedAt);
            Assert.Equal(JobStop.TimeLimit, row.Stopped);
            await h.Recorded("a:stopped:TimeLimit");
            await h.Logged($"Job {id} (Rask.Background.Tests.ChattyJob, lane pump-1) was stopped at its time limit 00:10:00");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_time_limit_read_from_the_job_holds_for_each_job_on_its_own()
    {
        await using var h = new JobsHarness(o => o.Limit<CappedJob>().After(job => TimeSpan.FromMinutes(job.Minutes)));
        await h.Queue.Enqueue(new CappedJob("short", 1), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new CappedJob("long", 3), Ct).InLane("pump-2");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("short");
            await h.Entered("long");
            var shortOne = await h.IdOf("short");
            var longOne = await h.IdOf("long");

            h.Clock.Advance(TimeSpan.FromMinutes(1));
            var first = await h.Processed(shortOne);
            await h.WatchedAsync();
            var longAfterOneMinute = await h.Row(longOne);
            h.Clock.Advance(TimeSpan.FromMinutes(2) - Second);
            await h.WatchedAsync();
            var longOneSecondShort = await h.Row(longOne);
            h.Clock.Advance(Second);
            var second = await h.Processed(longOne);

            Assert.Equal(JobStop.TimeLimit, first.Stopped);
            Assert.Null(longAfterOneMinute!.ProcessedAt);
            Assert.Null(longOneSecondShort!.ProcessedAt);
            Assert.Equal(JobStop.TimeLimit, second.Stopped);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopAfter_limits_every_job_whose_type_names_no_limit_of_its_own()
    {
        await using var h = new JobsHarness(o =>
        {
            o.StopAfter = TimeSpan.FromMinutes(2);
            o.Limit<CappedJob>().After(job => TimeSpan.FromMinutes(job.Minutes));
        });
        await h.Queue.Enqueue(new ParkJob("plain"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new CappedJob("own", 5), Ct).InLane("pump-2");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("plain");
            await h.Entered("own");
            var plain = await h.IdOf("plain");
            var own = await h.IdOf("own");

            h.Clock.Advance(TimeSpan.FromMinutes(2));
            var stopped = await h.Processed(plain);
            await h.WatchedAsync();
            var ownRow = await h.Row(own);

            Assert.Equal(JobStop.TimeLimit, stopped.Stopped);
            Assert.Null(ownRow!.ProcessedAt);
        }
        finally
        {
            h.Lanes.Release("own");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Without_a_limit_a_job_runs_on_however_long_it_takes()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            h.Clock.Advance(TimeSpan.FromDays(3));
            await h.WatchedAsync();
            var row = await h.Row(id);

            Assert.Null(row!.ProcessedAt);
            Assert.Null(row.Stopped);
        }
        finally
        {
            h.Lanes.Release("a");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(JobStop.Asked)]
    [InlineData(JobStop.TimeLimit)]
    [InlineData(JobStop.Idle)]
    [InlineData(JobStop.DoneElsewhere)]
    public async Task Every_kind_of_stop_frees_the_lane_and_its_next_job_runs_on_this_host(JobStop why)
    {
        await using var h = new JobsHarness(o =>
        {
            if (why == JobStop.TimeLimit)
            {
                o.Limit<ParkJob>().After(TimeSpan.FromMinutes(10));
            }

            if (why == JobStop.Idle)
            {
                o.Limit<ParkJob>().IdleFor(TimeSpan.FromMinutes(5));
            }
        });
        await h.Queue.Enqueue(new ParkJob("a"), Ct).InLane("pump-1");
        await h.Queue.Enqueue(new ParkJob("b"), Ct).InLane("pump-1");
        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var id = await h.IdOf("a");

            await Stop(h, id, why);
            await h.Recorded($"a:stopped:{why}");
            await h.Entered("b");
            var row = await h.Row(id);

            Assert.NotNull(row);
            Assert.NotNull(row.ProcessedAt);
            Assert.Null(row.Error);
            Assert.Equal(why == JobStop.DoneElsewhere, row.ClaimToken is not null); // their row, left as they left it
            Assert.Equal(why == JobStop.DoneElsewhere ? null : why, row.Stopped);
        }
        finally
        {
            h.Lanes.Release("b");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void A_limit_of_zero_or_less_is_refused_where_it_is_written()
    {
        var options = new JobsOptions();

        var after = Assert.Throws<ArgumentOutOfRangeException>(() => options.Limit<ParkJob>().After(TimeSpan.Zero));
        var idle = Assert.Throws<ArgumentOutOfRangeException>(() => options.Limit<ParkJob>().IdleFor(TimeSpan.FromSeconds(-1)));

        Assert.Equal("limit", after.ParamName);
        Assert.Equal("idle", idle.ParamName);
    }

    [Fact]
    public void The_last_limit_said_wins_and_the_bare_call_sets_nothing()
    {
        var options = new JobsOptions();

        options.Limit<CappedJob>();
        var none = options.LimitsOf("Rask.Background.Tests.CappedJob", new CappedJob("x", 7));
        options.Limit<CappedJob>().After(TimeSpan.FromMinutes(1)).After(job => TimeSpan.FromMinutes(job.Minutes));
        var computed = options.LimitsOf("Rask.Background.Tests.CappedJob", new CappedJob("x", 7));

        Assert.Equal(((TimeSpan?)null, (TimeSpan?)null), none);
        Assert.Equal(((TimeSpan?)TimeSpan.FromMinutes(7), (TimeSpan?)null), computed);
    }

    [Fact]
    public void A_limit_declared_on_the_battery_is_adopted_part_by_part()
    {
        var declared = new JobsOptions();
        declared.Limit<ParkJob>().IdleFor(TimeSpan.FromMinutes(5));
        var options = new JobsOptions();
        options.Limit<ParkJob>().After(TimeSpan.FromMinutes(30));

        options.AdoptLimits(declared);

        Assert.Equal(
            ((TimeSpan?)TimeSpan.FromMinutes(30), (TimeSpan?)TimeSpan.FromMinutes(5)),
            options.LimitsOf("Rask.Background.Tests.ParkJob", new ParkJob("x")));
    }

    // Makes job `id` stop for `why` — the way each kind of stop happens.
    private static async Task Stop(JobsHarness h, long id, JobStop why)
    {
        switch (why)
        {
            case JobStop.Asked:
                Assert.True(await h.Queue.Stop(id, Ct));
                break;
            case JobStop.TimeLimit:
                h.Clock.Advance(TimeSpan.FromMinutes(10));
                break;
            case JobStop.Idle:
                h.Clock.Advance(TimeSpan.FromMinutes(5));
                break;
            default:
                await using (var db = h.NewContext())
                {
                    var now = h.Clock.GetUtcNow().UtcDateTime;
                    await db.Set<Job>().Where(j => j.Id == id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ProcessedAt, now), Ct);
                }

                break;
        }
    }
}
