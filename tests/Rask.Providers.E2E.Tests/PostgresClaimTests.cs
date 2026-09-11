using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Providers.E2E.Tests;

public sealed record ProbeJob(string Value) : IBackgroundJob;

public sealed class ProbeJobHandler : ICommandHandler<ProbeJob>
{
    public Task HandleAsync(ProbeJob command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ClaimDbContext(DbContextOptions<ClaimDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_claim";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddRaskJobs();
    }
}

/// <summary>
/// The jobs claim, against a real PostgreSQL server configured the way an app would configure it.
/// </summary>
/// <remarks>
/// Everything else about leasing is proven deterministically on SQLite in <c>Rask.Jobs.Tests</c>. What only a
/// server can prove is the assumption underneath the whole design: that <c>UPDATE … WHERE &lt;claimable&gt;</c>
/// under READ COMMITTED blocks on a row another transaction is updating and then <b>re-evaluates the predicate
/// against the committed row version</b>, so the loser skips the row rather than overwriting the winner's
/// claim. It also runs through <c>UseRaskPostgres</c>, retrying strategy included — a strategy that refuses a
/// transaction the processor opened itself would fail here, not in production.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresClaimTests : IAsyncLifetime
{
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        if (!Postgres.Available)
        {
            return;
        }

        await using var db = NewContext();
        await Postgres.ResetSchemaAsync(db, ClaimDbContext.Schema);
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        if (Postgres.Available)
        {
            await using var db = NewContext();
            await Postgres.DropSchemaAsync(db, ClaimDbContext.Schema);
        }
    }

    [SkippableFact]
    public async Task Twenty_concurrent_claims_never_hand_the_same_job_to_two_instances()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        const int jobs = 200;
        const int instances = 20;
        await SeedAsync(jobs);

        var now = DateTime.UtcNow;
        var claimed = await ClaimTogetherAsync(instances, batchSize: 25, now);

        var ids = claimed.SelectMany(batch => batch.Select(j => j.Id)).ToList();

        // The invariant the design rests on: no id appears twice across every instance's batch.
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEmpty(ids);
        Assert.True(ids.Count <= jobs, $"claimed {ids.Count} of {jobs} jobs — more than exist.");

        // Deliberately NOT asserting that all 200 were claimed in one round: every instance runs the same
        // candidate query, so most legitimately lose to the same top-N, and how many rounds a drain takes is
        // pure interleaving.
    }

    [SkippableFact]
    public async Task An_expired_lease_is_reclaimed_by_exactly_one_of_many_instances()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        await SeedAsync(1);

        var now = DateTime.UtcNow;
        var (first, firstDb) = NewInstance(batchSize: 10);
        await using (firstDb)
        {
            Assert.Single(await first.ClaimAsync(firstDb, now, CancellationToken.None));
        }

        // That instance "dies" holding the row. After the lease expires, ten instances race to reclaim it.
        var afterExpiry = now + TimeSpan.FromMinutes(6);
        var reclaimed = await ClaimTogetherAsync(instances: 10, batchSize: 10, afterExpiry);

        Assert.Equal(1, reclaimed.Sum(batch => batch.Count));
    }

    [SkippableFact]
    public async Task The_largest_allowed_batch_claims_in_one_statement()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        // BatchSize is capped at 1000 because the claim sends the candidate ids as a Contains list. EF Core 10
        // pads that into a parameter list, so the cap is only safe if PostgreSQL accepts the widest one.
        await SeedAsync(1000);

        var (processor, context) = NewInstance(batchSize: 1000);
        await using (context)
        {
            var batch = await processor.ClaimAsync(context, DateTime.UtcNow, CancellationToken.None);

            Assert.Equal(1000, batch.Count);
            Assert.Equal(1000, batch.Select(j => j.Id).Distinct().Count());
        }
    }

    /// <summary>
    /// Builds every instance first, then releases all their claims at once.
    /// </summary>
    /// <remarks>
    /// Building a service provider is synchronous and slow next to a localhost claim, so starting each claim as
    /// its instance is built staggers them — one can commit before the next begins, and the test then passes
    /// without the contention it exists to prove. An awaited gate releases them together without parking a
    /// thread per instance.
    /// </remarks>
    private async Task<List<Job>[]> ClaimTogetherAsync(int instances, int batchSize, DateTime now)
    {
        var built = Enumerable.Range(0, instances).Select(_ => NewInstance(batchSize)).ToList();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var claims = built.Select(async instance =>
        {
            await start.Task;
            await using var db = instance.Context;
            return await instance.Processor.ClaimAsync(db, now, CancellationToken.None);
        }).ToList();

        start.SetResult();
        return await Task.WhenAll(claims);
    }

    private async Task SeedAsync(int count)
    {
        await using var db = NewContext();
        var now = DateTime.UtcNow.AddMinutes(-1);

        for (var i = 0; i < count; i++)
        {
            var (type, payload) = JobSerializerRegistry.Serialize(new ProbeJob($"j{i}"));
            db.Set<Job>().Add(new Job { Type = type, Payload = payload, RunAt = now, CreatedAt = now });
        }

        await db.SaveChangesAsync();
    }

    private static ClaimDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ClaimDbContext>().UseRaskPostgres(Postgres.Required).Options);

    /// <summary>A processor and its own context — two of these are two instances of the app.</summary>
    private (JobProcessor<ClaimDbContext> Processor, ClaimDbContext Context) NewInstance(int batchSize)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskJobs<ClaimDbContext>(o =>
        {
            o.BatchSize = batchSize;
            o.LeaseDuration = TimeSpan.FromMinutes(5);
        });
        services.AddDbContextFactory<ClaimDbContext>(o => o.UseRaskPostgres(Postgres.Required));

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var processor = provider.GetServices<IHostedService>().OfType<JobProcessor<ClaimDbContext>>().Single();
        return (processor, NewContext());
    }
}
