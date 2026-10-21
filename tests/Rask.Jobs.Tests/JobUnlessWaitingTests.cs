using Microsoft.EntityFrameworkCore;

namespace Rask.Background.Tests;

/// <summary>
/// <c>Jobs.Enqueue(job).InLane(id).UnlessWaiting()</c>: the same work is not queued twice while one copy still
/// waits, decided by the job table, so two hosts enqueueing at once still leave one row.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobUnlessWaitingTests
{
    [Fact]
    public async Task The_same_job_in_the_same_lane_is_skipped_while_the_first_one_waits()
    {
        await using var h = new JobsHarness();

        var first = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        var second = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, await h.CountJobsAsync());
    }

    [Fact]
    public async Task Once_the_waiting_job_is_claimed_the_same_job_waits_behind_it()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        await using (var db = h.NewContext())
        {
            Assert.Single(await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, TestContext.Current.CancellationToken));
        }

        var again = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        Assert.True(again);
        Assert.Equal(2, await h.CountJobsAsync());
    }

    [Fact]
    public async Task A_claimed_job_no_longer_carries_its_waiting_key()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        Assert.NotNull((await h.SingleJobAsync()).WaitingKey);

        await using (var db = h.NewContext())
        {
            await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, TestContext.Current.CancellationToken);
        }

        Assert.Null((await h.SingleJobAsync()).WaitingKey);
    }

    [Fact]
    public async Task A_different_payload_is_different_work()
    {
        await using var h = new JobsHarness();

        var ask = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        var chips = await h.Queue.Enqueue(new RecordJob("chips"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        Assert.True(ask);
        Assert.True(chips);
        Assert.Equal(2, await h.CountJobsAsync());
    }

    [Fact]
    public async Task The_same_job_in_another_lane_is_different_work()
    {
        await using var h = new JobsHarness();

        var seven = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        var eight = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-8").UnlessWaiting();

        Assert.True(seven);
        Assert.True(eight);
    }

    [Fact]
    public async Task A_plain_enqueue_still_adds_a_row_beside_a_waiting_one()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        var plain = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7");

        Assert.True(plain);
        Assert.Equal(2, await h.CountJobsAsync());
    }

    [Fact]
    public async Task The_table_itself_refuses_a_second_waiting_row_with_the_same_key()
    {
        await using var h = new JobsHarness();
        var (type, payload) = JobSerializerRegistry.Serialize(new RecordJob("ask"));
        var now = h.Clock.GetUtcNow().UtcDateTime;
        await using (var db = h.NewContext())
        {
            db.Set<Job>().Add(Job.Waiting(type, payload, now, "pump-7"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var other = h.NewContext();
        other.Set<Job>().Add(Job.Waiting(type, payload, now, "pump-7"));

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_hosts_enqueueing_the_same_work_at_once_leave_one_waiting_row()
    {
        await using var a = new JobsHarness();
        await using var b = new JobsHarness(dbPath: a.DbPath);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var enqueues = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            var host = i % 2 == 0 ? a : b;
            return await host.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        }).ToList();
        start.SetResult();
        var written = await Task.WhenAll(enqueues);

        Assert.Single(written, w => w);
        Assert.Equal(1, await a.CountJobsAsync());
    }

    [Fact]
    public async Task A_key_left_by_an_older_host_claim_is_cleared_and_the_work_waits_again()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        await ClaimAsAnOlderHost(h);

        var again = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        Assert.True(again);
        await using var db = h.NewContext();
        var rows = await db.Set<Job>().OrderBy(j => j.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].WaitingKey);
        Assert.NotNull(rows[1].WaitingKey);
    }

    [Fact]
    public async Task A_key_left_on_a_row_an_older_host_finished_is_cleared_and_the_work_waits_again()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        await using (var db = h.NewContext())
        {
            await db.Set<Job>().ExecuteUpdateAsync(
                s => s.SetProperty(j => j.ProcessedAt, DateTime.UtcNow), TestContext.Current.CancellationToken);
        }

        var again = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();

        Assert.True(again);
        Assert.Equal(2, await h.CountJobsAsync());
    }

    [Fact]
    public async Task Two_hosts_meeting_a_stale_key_at_once_leave_one_waiting_row()
    {
        await using var a = new JobsHarness();
        await using var b = new JobsHarness(dbPath: a.DbPath);
        await a.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        await ClaimAsAnOlderHost(a);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var enqueues = Enumerable.Range(0, 20).Select(async i =>
        {
            await start.Task;
            var host = i % 2 == 0 ? a : b;
            return await host.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        }).ToList();
        start.SetResult();
        var written = await Task.WhenAll(enqueues);

        Assert.Single(written, w => w);
        Assert.Equal(2, await a.CountJobsAsync());
    }

    // What a host on a Rask.Jobs from before WaitingKey does when it claims: the lease and the attempt, never the key.
    private static async Task ClaimAsAnOlderHost(JobsHarness h)
    {
        await using var db = h.NewContext();
        var until = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        await db.Set<Job>().ExecuteUpdateAsync(
            s => s
                .SetProperty(j => j.ClaimToken, Guid.NewGuid())
                .SetProperty(j => j.ClaimedUntil, until)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Two_hosts_that_both_passed_the_check_still_leave_one_waiting_row()
    {
        await using var h = new JobsHarness();
        var (type, payload) = JobSerializerRegistry.Serialize(new RecordJob("ask"));
        await using (var db = h.NewContext())
        {
            // The other host's insert landed between this host's check and its own insert.
            db.Set<Job>().Add(Job.Waiting(type, payload, h.Clock.GetUtcNow().UtcDateTime, "pump-7"));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var written = await ((JobBacklog<JobsDbContext>)h.Queue).Insert(
            new RecordJob("ask"), h.Clock.GetUtcNow().UtcDateTime, "pump-7", TestContext.Current.CancellationToken);

        Assert.False(written);
        Assert.Equal(1, await h.CountJobsAsync());
    }
}
