using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs.Tests;

/// <summary>
/// The drain persists each job's outcome on its own, so a row changing underneath the batch costs at most that
/// one row. Before this, the whole batch was written by a single <c>SaveChangesAsync</c> at the end: one
/// concurrently deleted row raised <see cref="DbUpdateConcurrencyException" />, rolled the transaction back, and
/// stripped <c>ProcessedAt</c> from every job in the batch that had already run — so they all ran again, every
/// poll, forever.
/// </summary>
public sealed class JobProcessorConcurrencyTests
{
    [Fact]
    public async Task A_row_deleted_mid_batch_does_not_strip_processed_state_from_the_rest()
    {
        await using var h = new JobsHarness();

        // Ordered by (RunAt, Id), so the saboteur is drained first and deletes the last row while the batch that
        // contains it is still in flight.
        await h.Queue.EnqueueAsync(new SaboteurJob());
        await h.Queue.EnqueueAsync(new RecordJob("b"));
        await h.Queue.EnqueueAsync(new RecordJob("c"));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            // The doomed row is gone and the two survivors are both marked done. Under the old batch-wide save
            // this never settles: the survivors keep losing ProcessedAt and re-running on every poll.
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().CountAsync() == 2
                       && await db.Set<Job>().AllAsync(j => j.ProcessedAt != null);
            });

            // Give the poll loop a few more cycles to prove the state is stable rather than momentary.
            await Task.Delay(200);
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        await using var db = h.NewContext();
        var jobs = await db.Set<Job>().OrderBy(j => j.Id).ToListAsync();

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.NotNull(j.ProcessedAt));
        Assert.All(jobs, j => Assert.Equal(0, j.Attempts));

        // The real payoff: each handler ran exactly once. A rolled-back batch re-runs everything it had already
        // executed, so duplicates here are the user-visible symptom of the bug.
        Assert.Equal(1, h.Recorder.Values.Count(v => v == "b"));
        Assert.Equal(1, h.Recorder.Values.Count(v => v == "c"));
    }

    [Fact]
    public async Task A_faulting_cycle_does_not_stop_the_processor()
    {
        await using var h = new JobsHarness();

        await h.Queue.EnqueueAsync(new SaboteurJob());
        await h.Queue.EnqueueAsync(new RecordJob("first"));

        await h.Processor.StartAsync(CancellationToken.None);
        try
        {
            // The saboteur deletes "first" mid-drain, so that row's save throws out of the drain. Without the
            // per-cycle guard that exception faults the BackgroundService — and with the default
            // BackgroundServiceExceptionBehavior.StopHost, takes the whole application down with it.
            await h.WaitUntilAsync(async () =>
            {
                await using var db = h.NewContext();
                return await db.Set<Job>().CountAsync() == 1;
            });

            // The loop must still be alive: work enqueued after the fault still gets drained.
            await h.Queue.EnqueueAsync(new RecordJob("after-the-fault"));
            await h.WaitUntilAsync(() => Task.FromResult(h.Recorder.Values.Contains("after-the-fault")));
        }
        finally
        {
            await h.Processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains("after-the-fault", h.Recorder.Values);
    }
}
