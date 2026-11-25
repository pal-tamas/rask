using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
///     A batch (#1368): <c>Jobs.Enqueue(jobs)</c> writes every row in one save — all of them or none — stamped with one batch,
///     each titled by <c>.Noticed(j =&gt; …)</c>, and the board shows it as one piece of work.
/// </summary>
public sealed class JobBatchTests
{
    private static readonly Guid Alice = Guid.Parse("0e4b6c1e-7b52-4f3c-9d2f-1a1a1a1a1a1a");
    private static readonly Guid Acme = Guid.Parse("9a3f6c55-0d4e-4a7b-b1c2-3c3c3c3c3c3c");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_batch_writes_every_row_with_one_batch_and_each_its_own_title()
    {
        await using var h = new JobsHarness();

        var written = await Written(h, ["a.csv", "b.csv", "c.csv"], b => b.Noticed(j => $"Import of {j.Tag}").InLane("partner-7"));

        await using var db = h.NewContext();
        var rows = await db.Set<Job>().AsNoTracking().OrderBy(j => j.Id).ToListAsync(Ct);
        Assert.Equal(3, written);
        Assert.Equal(["Import of a.csv", "Import of b.csv", "Import of c.csv"], rows.Select(r => r.Notice));
        Assert.Single(rows.Select(r => r.Batch).Distinct());
        Assert.NotNull(rows[0].Batch);
        Assert.All(rows, r => Assert.Equal("partner-7", r.Lane));
        Assert.All(rows, r => Assert.Equal(Alice, r.UserId));
    }

    [Fact]
    public async Task A_batch_whose_write_fails_leaves_no_row_at_all()
    {
        await using var h = new JobsHarness();
        await using (var db = h.NewContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER no_boom BEFORE INSERT ON Job WHEN NEW.Payload LIKE '%boom%' BEGIN SELECT RAISE(ABORT, 'boom'); END;",
                cancellationToken: Ct);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => Written(h, ["a.csv", "boom", "c.csv"], b => b.Noticed(j => j.Tag)));

        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task A_batch_inside_a_transaction_that_rolls_back_leaves_no_row()
    {
        await using var h = new JobsHarness();
        using var facade = Db.Use(h.Get<IDbContextFactory<JobsDbContext>>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Written(h, ["a.csv", "b.csv"], b => b);
            throw new InvalidOperationException("the press was undone");
        }, cancellationToken: Ct));

        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task An_empty_batch_writes_nothing_and_answers_none()
    {
        await using var h = new JobsHarness();

        var written = await Written(h, [], b => b);

        Assert.Equal(0, written);
        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task A_shared_batch_with_no_tenant_throws_before_anything_is_written()
    {
        await using var h = new JobsHarness();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Written(h, ["a.csv"], b => b.Noticed(j => j.Tag).ByTenant()));

        Assert.Contains("Jobs.Enqueue(jobs).Noticed(…).ByTenant()", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task The_board_shows_a_batch_as_one_piece_of_ended_work_however_many_jobs_it_holds()
    {
        await using var h = new JobsHarness();
        using (Tenant.Use(Acme))
        {
            await Written(h, [.. Enumerable.Range(1, 12).Select(i => $"{i}.csv")], b => b.Noticed(j => j.Tag));
            for (var i = 0; i < 10; i++)
            {
                await Single(h, $"single-{i}");
            }
        }

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 22);
        var board = await Board(h);

        Assert.Equal(10, board.Select(w => w.Batch is { } b ? (object)b : w.Id).Distinct().Count());
        Assert.True(board.All(w => w.HasEnded));
    }

    [Fact]
    public async Task The_fake_records_a_batch_with_one_batch_and_runs_it()
    {
        await using var services = new ServiceCollection().AddRaskCqrs().AddSingleton(new Recorder()).BuildServiceProvider();
        using var ambient = Ambient.Enter(services);
        using var jobs = Jobs.Fake();

        int written;
        using (Current.UseUser(Alice))
        {
            written = await Jobs.Enqueue([new OpenImport("a"), new OpenImport("b")], Ct).Noticed(j => j.Tag);
        }

        await jobs.Run(Ct);

        Assert.Equal(2, written);
        Assert.Equal(["a", "b"], services.GetRequiredService<Recorder>().Values);
        jobs.Enqueued<OpenImport>().Twice();
    }

    // Alice enqueues a batch of one job per tag, worded by the test.
    private static async Task<int> Written(
        JobsHarness h, IEnumerable<string> tags, Func<Enqueuing<OpenImport>, Enqueuing<OpenImport>> word)
    {
        using var who = Current.UseUser(Alice);
        return await word(new Enqueuing<OpenImport>(h.Queue, [.. tags.Select(t => new OpenImport(t))], Ct));
    }

    private static async Task Single(JobsHarness h, string tag)
    {
        using var who = Current.UseUser(Alice);
        await h.Queue.Enqueue(new OpenImport(tag), Ct).Noticed(tag);
    }

    private static async Task<IReadOnlyList<WorkReported>> Board(JobsHarness h)
    {
        using var scope = h.Get<IServiceScopeFactory>().CreateScope();
        using var who = Current.UseUser(Alice);
        using var where = Tenant.Use(Acme);
        return await scope.ServiceProvider.GetRequiredService<IWorkBoard>().Mine(lane: null, Ct);
    }
}
