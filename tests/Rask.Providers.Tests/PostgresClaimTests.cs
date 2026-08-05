using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;
using Rask.Jobs;

namespace Rask.Providers.Tests;

public sealed record ProbeJob(string Value) : IJob;

public sealed class ProbeJobHandler : ICommandHandler<ProbeJob>
{
    public Task HandleAsync(ProbeJob command, CancellationToken cancellationToken) => Task.CompletedTask;
}

public class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

/// <summary>
/// The claim, against a real PostgreSQL server.
/// </summary>
/// <remarks>
/// Everything else about leasing is proven deterministically on SQLite in <c>Rask.Jobs.Tests</c>. What can
/// only be proven here is the load-bearing assumption underneath the whole design: that
/// <c>UPDATE … WHERE &lt;claimable&gt;</c> under PostgreSQL's default READ COMMITTED blocks on a row another
/// transaction is updating and then <b>re-evaluates the predicate against the committed row version</b>
/// (EvaluatePlanQual), so the loser skips the row rather than overwriting the winner's claim. That is a
/// claim about the server, not about Rask, and asserting it without testing it would be exactly the kind of
/// thing that is wrong in production and nowhere else.
/// </remarks>
[Collection("postgres")]
public sealed class PostgresClaimTests : IAsyncLifetime
{
    private readonly string _schema = "rask_test_" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        await using var db = NewContext();

        // A schema name is an identifier, so it cannot be a parameter. This one is "rask_test_" plus 12 hex
        // characters of a Guid this class generated — no external input reaches it.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{_schema}\";");
#pragma warning restore EF1002
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var db = NewContext())
        {
#pragma warning disable EF1002 // Identifier, not a value — see InitializeAsync.
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;");
#pragma warning restore EF1002
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    [PostgresFact]
    public async Task Twenty_concurrent_claims_never_hand_the_same_job_to_two_instances()
    {
        const int jobs = 200;
        const int instances = 20;

        await SeedAsync(jobs);

        var now = DateTime.UtcNow;
        var claimed = await Task.WhenAll(Enumerable.Range(0, instances).Select(async _ =>
        {
            var (processor, context) = NewInstance(batchSize: 25);
            await using var db = context;
            return await processor.ClaimAsync(db, now, CancellationToken.None);
        }));

        var ids = claimed.SelectMany(batch => batch.Select(j => j.Id)).ToList();

        // The assertion the design rests on: no id appears twice across every instance's batch.
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEmpty(ids);

        // And nothing was lost: 20 instances × 25 covers all 200, so every job ended up claimed exactly once.
        Assert.Equal(jobs, ids.Count);
    }

    [PostgresFact]
    public async Task An_expired_lease_is_reclaimed_by_exactly_one_of_many_instances()
    {
        await SeedAsync(1);

        var now = DateTime.UtcNow;
        var (first, firstDb) = NewInstance(batchSize: 10);
        await using (firstDb)
        {
            Assert.Single(await first.ClaimAsync(firstDb, now, CancellationToken.None));
        }

        // That instance "dies" holding the row. After the lease expires, ten instances race to reclaim it.
        var afterExpiry = now + TimeSpan.FromMinutes(6);
        var reclaimed = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var (processor, context) = NewInstance(batchSize: 10);
            await using var db = context;
            return await processor.ClaimAsync(db, afterExpiry, CancellationToken.None);
        }));

        Assert.Equal(1, reclaimed.Sum(batch => batch.Count));
    }

    [PostgresFact]
    public async Task The_candidate_id_list_is_not_sent_as_one_parameter_per_id()
    {
        // BatchSize is capped at 1000, which is only safe if Contains translates to a set-valued parameter
        // (= ANY(@p)) rather than a 1000-wide IN list — PostgreSQL's parameter ceiling is 65535, but the
        // generated SQL is what decides whether that matters.
        await SeedAsync(50);

        var messages = new List<string>();
        var (processor, context) = NewInstance(batchSize: 50, sql: messages);
        await using (context)
        {
            Assert.Equal(50, (await processor.ClaimAsync(context, DateTime.UtcNow, CancellationToken.None)).Count);
        }

        var update = messages.Find(m => m.Contains("UPDATE", StringComparison.Ordinal));
        Assert.NotNull(update);
        Assert.DoesNotContain("@p49", update, StringComparison.Ordinal);
    }

    private async Task SeedAsync(int count)
    {
        var (_, db) = NewInstance(batchSize: 1);
        await using (db)
        {
            var now = DateTime.UtcNow.AddMinutes(-1);
            for (var i = 0; i < count; i++)
            {
                var (type, payload) = JobSerializerRegistry.Serialize(new ProbeJob($"j{i}"));
                db.Set<Job>().Add(new Job { Type = type, Payload = payload, RunAt = now, CreatedAt = now });
            }

            await db.SaveChangesAsync();
        }
    }

    private ProbeDbContext NewContext(List<string>? sql = null)
    {
        var builder = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(Postgres.Required, o => o.MigrationsHistoryTable("__EFMigrationsHistory", _schema));

        if (sql is not null)
        {
            builder.LogTo(line => { lock (sql) { sql.Add(line); } }, [DbLoggerCategory.Database.Command.Name], LogLevel.Information);
        }

        return new SchemaScopedContext(builder.Options, _schema);
    }

    /// <summary>A processor and its own context — two of these are two "instances" of the app.</summary>
    private (JobProcessor<ProbeDbContext> Processor, ProbeDbContext Context) NewInstance(int batchSize, List<string>? sql = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskJobs<ProbeDbContext>(o =>
        {
            o.BatchSize = batchSize;
            o.LeaseDuration = TimeSpan.FromMinutes(5);
        });
        services.AddDbContextFactory<ProbeDbContext>(_ => { });
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var processor = provider.GetServices<IHostedService>().OfType<JobProcessor<ProbeDbContext>>().Single();
        return (processor, NewContext(sql));
    }

    /// <summary>Pins every table to this test class's own schema, so classes can run against one server.</summary>
    private sealed class SchemaScopedContext(DbContextOptions<ProbeDbContext> options, string schema) : ProbeDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(schema);
        }
    }
}

[CollectionDefinition("postgres", DisableParallelization = true)]
public sealed class PostgresCollection;
