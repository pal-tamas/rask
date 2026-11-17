using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
///     A job its enqueuer watches (#1293): <c>.Noticed(title)</c> puts the title on its row, <c>Jobs.Report(done, of)</c> moves
///     its bar — published at most four times a second and on the row once a poll — and only its enqueuer, in the tenant they
///     enqueued it in, ever hears of it.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobWorkNoticeTests
{
    private static readonly Guid Alice = Guid.Parse("0e4b6c1e-7b52-4f3c-9d2f-1a1a1a1a1a1a");
    private static readonly Guid Bob = Guid.Parse("5d0f2a77-3c11-4b8e-8e0b-2b2b2b2b2b2b");
    private static readonly Guid Acme = Guid.Parse("9a3f6c55-0d4e-4a7b-b1c2-3c3c3c3c3c3c");
    private static readonly Guid Globex = Guid.Parse("c7e1d9a2-6b3f-4e8d-a5f4-4d4d4d4d4d4d");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_noticed_job_says_it_runs_how_far_along_it_is_and_that_it_is_done()
    {
        await using var h = new JobsHarness();
        await using var heard = await HeardWork.Everything(h);
        await Enqueue(h, Alice, new BurstJob("a", Burst: 1, Of: 4), "Import of prices.csv");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            h.Lanes.Release("a");
            await heard.Hears(h, w => w.State == WorkState.Done);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        var said = heard.Heard;
        Assert.Equal(WorkState.Running, said[0].State);
        Assert.Null(said[0].Done);
        Assert.Contains(said, w => w is { State: WorkState.Running, Done: 1, Of: 4, Progress: "1 of 4 rows" });
        Assert.Equal(WorkState.Done, said[^1].State);
        Assert.All(said, w => Assert.Equal("Import of prices.csv", w.Title));
        Assert.All(said, w => Assert.Equal(Alice, w.Owner));
    }

    [Fact]
    public async Task Reports_closer_than_a_quarter_second_say_the_first_at_once_and_the_latest_at_the_next_poll()
    {
        await using var h = new JobsHarness();
        await using var heard = await HeardWork.Everything(h);
        await Enqueue(h, Alice, new BurstJob("a", Burst: 50, Of: 50), "Import");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            await heard.Hears(h, w => w.Done == 50);
            h.Lanes.Release("a");
            await heard.Hears(h, w => w.State == WorkState.Done);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        var reports = heard.Heard.Where(w => w is { State: WorkState.Running, Done: not null }).Select(w => w.Done).ToList();
        Assert.Equal([1, 50], reports);
    }

    [Fact]
    public async Task What_a_job_last_reported_is_on_its_row_after_a_poll()
    {
        await using var h = new JobsHarness();
        await Enqueue(h, Alice, new BurstJob("a", Burst: 3, Of: 10), "Import");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            await h.WatchedAsync();
            var id = await h.IdOf("a");
            var row = await h.Row(id);

            Assert.Equal("Import", row!.Notice);
            Assert.Equal(3, row.Done);
            Assert.Equal(10, row.Of);
            Assert.Equal("3 of 10 rows", row.Progress);
        }
        finally
        {
            h.Lanes.Release("a");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_lane_view_shows_how_far_along_its_running_job_is()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new BurstJob("a", Burst: 2, Of: 8), Ct).InLane("pump-1");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            var lane = await h.Queue.Lanes(["pump-1"], Ct);

            Assert.Equal(2, lane["pump-1"].Running!.Done);
            Assert.Equal(8, lane["pump-1"].Running!.Of);
            Assert.Equal("2 of 8 rows", lane["pump-1"].Running!.Progress);
        }
        finally
        {
            h.Lanes.Release("a");
            await h.Processor.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Another_users_work_and_the_same_users_work_in_another_tenant_never_arrive()
    {
        await using var h = new JobsHarness();
        await using var alice = await HeardWork.Own(h, Alice, Acme);
        await using var aliceElsewhere = await HeardWork.Own(h, Alice, Globex);
        await using var bob = await HeardWork.Own(h, Bob, Acme);
        await Enqueue(h, Alice, new BurstJob("a", Burst: 1, Of: 2), "Alice's import", Acme);

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            h.Lanes.Release("a");
            await alice.Hears(h, w => w.State == WorkState.Done);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.All(alice.Heard, w => Assert.Equal("Alice's import", w.Title));
        Assert.Empty(aliceElsewhere.Heard);
        Assert.Empty(bob.Heard);
    }

    [Fact]
    public async Task Nobody_signed_in_may_watch_anyones_work()
    {
        await using var h = new JobsHarness();

        await using var nobody = await HeardWork.Own(h, user: null);

        Assert.IsType<UnauthorizedAccessException>(nobody.Refused);
    }

    [Fact]
    public async Task A_job_nobody_watches_says_nothing()
    {
        await using var h = new JobsHarness();
        await using var heard = await HeardWork.Everything(h);
        await Enqueue(h, Alice, new BurstJob("a", Burst: 1, Of: 2), title: null);
        await Enqueue(h, user: null, new BurstJob("b", Burst: 1, Of: 2), "Import for nobody");

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Entered("a");
            h.Lanes.Release("a");
            await h.Entered("b");
            h.Lanes.Release("b");
            await h.Processed(await h.IdOf("a"));
            await h.Processed(await h.IdOf("b"));
            await h.WatchedAsync();
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Empty(heard.Heard);
    }

    [Fact]
    public async Task A_job_that_fails_for_good_says_failed_and_never_why()
    {
        await using var h = new JobsHarness(o => o.MaxAttempts = 1);
        await using var heard = await HeardWork.Own(h, Alice);
        await Enqueue(h, Alice, new BurstFailJob("f"), "Export");

        await h.RunUntilAsync(() => heard.Heard.Any(w => w.State == WorkState.Failed));

        var failed = heard.Heard.Last();
        Assert.Equal(WorkState.Failed, failed.State);
        Assert.Equal(1, failed.Done);
        Assert.DoesNotContain(heard.Heard, w => w.Progress?.Contains("secret", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task The_board_reads_the_users_own_running_and_ended_work_and_nobody_elses()
    {
        await using var h = new JobsHarness(o => o.MaxAttempts = 1);
        await Enqueue(h, Alice, new BurstJob("run", Burst: 1, Of: 5), "Running import", lane: "imports");
        await Enqueue(h, Alice, new RecordJob("done"), "Finished import");
        await Enqueue(h, Alice, new BurstFailJob("fail"), "Broken export");
        await Enqueue(h, Bob, new RecordJob("bob"), "Bob's import");

        await h.Processor.StartAsync(CancellationToken.None);
        IReadOnlyList<WorkReported> board;
        try
        {
            await h.Entered("run");
            await h.WaitUntilAsync(async () => (await Board(h, Alice)).Count(w => w.HasEnded) == 2);
            board = await Board(h, Alice);
        }
        finally
        {
            h.Lanes.Release("run");
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Equal(3, board.Count);
        Assert.Equal("Running import", board[0].Title);
        Assert.Equal(WorkState.Running, board[0].State);
        Assert.Equal(1, board[0].Done);
        Assert.Contains(board, w => w is { Title: "Finished import", State: WorkState.Done });
        Assert.Contains(board, w => w is { Title: "Broken export", State: WorkState.Failed });
        Assert.Empty(await Board(h, user: null));
    }

    [Theory]
    [InlineData("Notice")]
    [InlineData("Progress")]
    [InlineData("Done")]
    [InlineData("Of")]
    public async Task A_database_without_the_work_columns_says_which_commands_to_run(string column)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-nowork-{Guid.NewGuid():N}.db");
        await using var h = new JobsHarness(dbPath: path);
        await h.Queue.Enqueue(new RecordJob("waiting"), Ct);
        await using (var db = h.NewContext())
        {
#pragma warning disable EF1002 // The column is one of four constants above, not input.
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE Job DROP COLUMN \"{column}\";", cancellationToken: Ct);
#pragma warning restore EF1002
        }

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            await h.Logged("rask db add AddJobProgress && rask db update");
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.DoesNotContain(h.Logs, l => l.Contains("Job processing cycle failed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_count_below_zero_is_refused_and_a_title_too_long_is_refused()
    {
        var negative = Assert.Throws<ArgumentOutOfRangeException>(() => Jobs.Report(-1, 10));
        var tooLong = Assert.Throws<ArgumentOutOfRangeException>(() => Jobs.Enqueue(new TickJob(), Ct).Noticed(new string('x', 257)));

        Assert.Equal("done", negative.ParamName);
        Assert.Equal("title", tooLong.ParamName);
    }

    [Fact]
    public async Task The_fake_says_a_noticed_jobs_work_as_the_processor_would()
    {
        await using var services = new ServiceCollection().AddRaskCqrs().AddSingleton(new LaneGate()).BuildServiceProvider();
        using var ambient = Ambient.Enter(services);
        using var jobs = Jobs.Fake();
        var heard = new List<WorkReported>();
        using var stop = new CancellationTokenSource();
        var listening = Listen(services.GetRequiredService<IDispatcher>(), heard, stop.Token);
        await Rask.TestSupport.WaitFor.True(() => services.GetRequiredService<EventFeed>().ListenerCount(typeof(WorkReported)) > 0);
        using (Current.UseUser(Alice))
        {
            await Jobs.Enqueue(new BurstFailJob("f"), Ct).Noticed("Export");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => jobs.Run(Ct));
        await stop.CancelAsync();
        await listening;

        Assert.Equal([WorkState.Running, WorkState.Running, WorkState.Failed], heard.Select(w => w.State));
        Assert.Equal(1, heard[1].Done);
        Assert.All(heard, w => Assert.Equal(Alice, w.Owner));
    }

    private static async Task Listen(IDispatcher dispatcher, List<WorkReported> heard, CancellationToken stop)
    {
        var reading = dispatcher.Subscribe<WorkReported>(stop).GetAsyncEnumerator(stop);
        var next = reading.MoveNextAsync();
        try
        {
            while (await next)
            {
                heard.Add(reading.Current);
                next = reading.MoveNextAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Closed by the test.
        }
        finally
        {
            await reading.DisposeAsync();
        }
    }

    private static async Task Enqueue(JobsHarness h, Guid? user, IJob job, string? title, Guid? tenant = null, string? lane = null)
    {
        using var who = Current.UseUser(user);
        using var where = tenant is { } t ? Tenant.Use(t) : null;
        var enqueuing = lane is null ? h.Queue.Enqueue(job, Ct) : h.Queue.Enqueue(job, Ct).InLane(lane);
        await (title is null ? enqueuing : enqueuing.Noticed(title));
    }

    private static async Task<IReadOnlyList<WorkReported>> Board(JobsHarness h, Guid? user)
    {
        using var scope = h.Get<IServiceScopeFactory>().CreateScope();
        using var who = Current.UseUser(user);
        return await scope.ServiceProvider.GetRequiredService<IWorkBoard>().Mine(lane: null, Ct);
    }
}
