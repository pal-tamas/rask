using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
///     A job enqueued inside <c>Db.Transaction</c> is a row in that transaction: nobody sees it before the commit, and a
///     rollback leaves nothing to run.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobDbTransactionTests : IDisposable
{
    public void Dispose() => Db.Reset();

    [Fact]
    public async Task A_job_enqueued_inside_is_there_only_once_the_work_commits()
    {
        await using var h = Configured(new JobsHarness());
        var seenBeforeCommit = -1;

        await Db.Transaction(async () =>
        {
            await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken);
            seenBeforeCommit = await h.CountJobsAsync();
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, seenBeforeCommit);
        Assert.Equal(1, await h.CountJobsAsync());
    }

    [Fact]
    public async Task A_rollback_leaves_no_job_to_run()
    {
        await using var h = Configured(new JobsHarness());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken);
            throw new InvalidOperationException("out of stock");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task Unless_waiting_answers_false_inside_and_the_rest_of_the_work_still_commits()
    {
        await using var h = Configured(new JobsHarness());
        await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
        var again = true;

        await Db.Transaction(async () =>
        {
            again = await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken).InLane("pump-7").UnlessWaiting();
            await h.Queue.Enqueue(new RecordJob("chips"), TestContext.Current.CancellationToken);
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(again);
        Assert.Equal(2, await h.CountJobsAsync());
    }

    [Fact]
    public async Task A_job_table_in_another_database_is_refused_rather_than_written_outside()
    {
        await using var h = Configured(new JobsHarness());
        await using var other = new JobsHarness();
        Db.Configure(other.Get<IDbContextFactory<JobsDbContext>>());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(
            async () => await h.Queue.Enqueue(new RecordJob("ask"), TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another database", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await h.CountJobsAsync());
    }

    private static JobsHarness Configured(JobsHarness h)
    {
        using (var db = h.NewContext())
        {
            db.Database.EnsureCreated();
        }

        Db.Configure(h.Get<IDbContextFactory<JobsDbContext>>());
        return h;
    }
}
