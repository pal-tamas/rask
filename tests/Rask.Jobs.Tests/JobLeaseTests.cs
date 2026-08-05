using Microsoft.EntityFrameworkCore;
using Rask.Jobs;

namespace Rask.Jobs.Tests;

/// <summary>
/// Leased claiming: what stops two processor instances running the same job.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> a race. Two harnesses share one SQLite file, but the interleaving is produced by
/// calling <c>ClaimAsync</c> in a chosen order rather than by starting two background services and hoping.
/// A stress test wearing a unit test's clothes proves nothing on the run where the timing happens to work;
/// these fail every time if the claim is wrong. The one genuine race lives in
/// <see cref="JobProcessorConcurrencyTests"/>.
/// </remarks>
public sealed class JobLeaseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Two instances over one database file, each with its own clock started together.</summary>
    private static (JobsHarness A, JobsHarness B) Pair(Action<JobOptions>? configure = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-lease-test-{Guid.NewGuid():N}.db");
        var a = new JobsHarness(configure, Start, path);
        var b = new JobsHarness(configure, Start, path);
        return (a, b);
    }

    private static Task<List<Job>> ClaimAsync(JobsHarness h)
    {
        var db = h.NewContext();
        return h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, CancellationToken.None);
    }

    [Fact]
    public async Task Two_instances_claim_disjoint_batches()
    {
        var (a, b) = Pair();
        await using var _ = a;
        await using var __ = b;
        for (var i = 0; i < 6; i++)
        {
            await a.Queue.EnqueueAsync(new RecordJob("x"));
        }

        var first = await ClaimAsync(a);
        var second = await ClaimAsync(b);

        // The whole point: the second instance finds nothing left to take.
        Assert.Equal(6, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public async Task A_second_instance_claims_what_the_first_left()
    {
        var (a, b) = Pair(o => o.BatchSize = 3);
        await using var _ = a;
        await using var __ = b;
        for (var i = 0; i < 6; i++)
        {
            await a.Queue.EnqueueAsync(new RecordJob("x"));
        }

        var first = await ClaimAsync(a);
        var second = await ClaimAsync(b);

        Assert.Equal(3, first.Count);
        Assert.Equal(3, second.Count);
        Assert.Empty(first.Select(j => j.Id).Intersect(second.Select(j => j.Id)));
        Assert.Equal(6, first.Concat(second).Select(j => j.Id).Distinct().Count());
    }

    [Fact]
    public async Task An_expired_lease_is_reclaimed_without_a_sweeper()
    {
        // The reason the claim tests lease expiry rather than "is the token null": an instance that dies
        // never clears its token, so a null test would hide this work forever.
        var (a, b) = Pair(o => o.LeaseDuration = TimeSpan.FromMinutes(5));
        await using var _ = a;
        await using var __ = b;
        await a.Queue.EnqueueAsync(new RecordJob("x"));

        var first = await ClaimAsync(a);
        Assert.Single(first);

        // a "dies" holding the row — nothing releases it.
        Assert.Empty(await ClaimAsync(b));

        b.Clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var reclaimed = await ClaimAsync(b);

        Assert.Single(reclaimed);
        Assert.Equal(first[0].Id, reclaimed[0].Id);
        Assert.Equal(2, reclaimed[0].Attempts); // the crashed attempt still counted
    }

    [Fact]
    public async Task A_crash_loop_dead_letters_instead_of_retrying_forever()
    {
        // Attempts is incremented by the claim, so a job that takes the process down with it every time
        // still reaches MaxAttempts. Counting only *failures* would retry it forever.
        var (a, b) = Pair(o =>
        {
            o.MaxAttempts = 3;
            o.LeaseDuration = TimeSpan.FromMinutes(1);
        });
        await using var _ = a;
        await using var __ = b;
        await a.Queue.EnqueueAsync(new RecordJob("x"));

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Single(await ClaimAsync(a));  // claimed, then "crashed" — never completed, never released
            a.Clock.Advance(TimeSpan.FromMinutes(2));
        }

        Assert.Empty(await ClaimAsync(a));

        await using var db = a.NewContext();
        var job = await db.Set<Job>().SingleAsync();
        Assert.Equal(3, job.Attempts);
        Assert.Null(job.ProcessedAt);
    }

    [Fact]
    public async Task An_instance_that_lost_its_lease_cannot_stamp_over_the_winner()
    {
        // The IsConcurrencyToken fence. Without it the slow instance would overwrite the outcome of
        // whichever instance actually owns the row now.
        var (a, b) = Pair(o => o.LeaseDuration = TimeSpan.FromMinutes(5));
        await using var _ = a;
        await using var __ = b;
        await a.Queue.EnqueueAsync(new RecordJob("x"));

        await using var slow = a.NewContext();
        var mine = await a.Jobs.ClaimAsync(slow, a.Clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        Assert.Single(mine);

        b.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Single(await ClaimAsync(b)); // b takes it over

        mine[0].ProcessedAt = a.Clock.GetUtcNow().UtcDateTime;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => slow.SaveChangesAsync());
    }

    [Fact]
    public async Task Stopping_releases_the_lease_immediately()
    {
        // Otherwise a rolling deploy parks a whole batch for the full lease duration.
        var (a, b) = Pair(o => o.LeaseDuration = TimeSpan.FromMinutes(30));
        await using var _ = a;
        await using var __ = b;
        await a.Queue.EnqueueAsync(new RecordJob("x"));

        Assert.Single(await ClaimAsync(a));
        Assert.Empty(await ClaimAsync(b));

        await a.Jobs.StopAsync(CancellationToken.None);

        Assert.Single(await ClaimAsync(b));
    }

    [Fact]
    public async Task Two_instances_do_not_double_enqueue_a_recurring_job()
    {
        // A hazard the drain's lease does nothing about: both instances read the same RecurringJobState,
        // both see it due, and both enqueue. The two LastEnqueuedAt writes then race with nothing to detect
        // the conflict, so neither loses — N× every recurring job, for as long as the app runs.
        var path = Path.Combine(Path.GetTempPath(), $"rask-lease-test-{Guid.NewGuid():N}.db");
        await using var a = new JobsHarness(
            o => o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()), Start, path);
        await using var b = new JobsHarness(
            o => o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()), Start, path);

        await a.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);
        await b.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);

        Assert.Equal(1, await a.CountJobsAsync());

        // ...and still one per interval on the next tick, not one per instance.
        a.Clock.Advance(TimeSpan.FromHours(1));
        b.Clock.Advance(TimeSpan.FromHours(1));
        await a.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);
        await b.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);

        Assert.Equal(2, await a.CountJobsAsync());
    }

    [Fact]
    public async Task The_first_ever_recurring_tick_is_enqueued_exactly_once()
    {
        // The first tick is the one case with no state row to compare-and-swap against: both instances try
        // to create it and one loses on the primary key. It must not enqueue on the way past — and, the
        // other half, the winner must not be blocked by a NULL comparison that is never true.
        var path = Path.Combine(Path.GetTempPath(), $"rask-lease-test-{Guid.NewGuid():N}.db");
        await using var a = new JobsHarness(
            o => o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()), Start, path);
        await using var b = new JobsHarness(
            o => o.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob()), Start, path);

        await a.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);
        await b.Jobs.EnqueueDueRecurringAsync(CancellationToken.None);

        Assert.Equal(1, await a.CountJobsAsync());
    }

    [Fact]
    public async Task A_claim_leaves_no_lease_on_a_completed_job()
    {
        // A finished job must not sit there looking claimed until its lease runs out.
        await using var h = new JobsHarness();
        await h.Queue.EnqueueAsync(new RecordJob("x"));

        await h.RunUntilAsync(() => h.Recorder.Values.Count > 0);

        await using var db = h.NewContext();
        var job = await db.Set<Job>().SingleAsync();
        Assert.NotNull(job.ProcessedAt);
        Assert.Null(job.ClaimToken);
        Assert.Null(job.ClaimedUntil);
    }
}
