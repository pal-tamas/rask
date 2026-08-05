using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Cqrs;
using Rask.Jobs;

namespace Rask.Providers.Tests;

/// <summary>
/// A fact that needs a real SQL Server, named by <c>RASK_MSSQL_TEST_DB</c>.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(SqlServer.ConnectionString))
        {
            Skip = "Needs a SQL Server: run scripts/run-providers-local.sh (or set RASK_MSSQL_TEST_DB).";
        }
    }
}

internal static class SqlServer
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("RASK_MSSQL_TEST_DB");

    public static string Required =>
        ConnectionString ?? throw new InvalidOperationException("RASK_MSSQL_TEST_DB is not set.");
}

/// <summary>
/// The claim, against a real SQL Server.
/// </summary>
/// <remarks>
/// Same purpose as <see cref="PostgresClaimTests"/>, different engine: under both locking READ COMMITTED and
/// RCSI, a data-modification statement takes update locks, blocks on a row another transaction is changing,
/// and re-reads it before applying its own predicate — so the loser skips the row. Under explicit
/// <c>SNAPSHOT</c> isolation SQL Server raises error 3960 instead of silently double-claiming, which the
/// processor's per-cycle catch turns into "retry next poll" — safe by failure rather than safe by design,
/// and worth knowing.
/// </remarks>
[Collection("sqlserver")]
public sealed class SqlServerClaimTests : IAsyncLifetime
{
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>
    /// Its own database rather than its own schema, unlike the PostgreSQL suite. EF can create and drop a
    /// SQL Server database outright, which avoids needing DDL of our own — and, more to the point, avoids a
    /// <c>EnsureDeletedAsync</c> that could ever be pointed at a shared database.
    /// </summary>
    private readonly string _database = "rask_test_" + Guid.NewGuid().ToString("N")[..12];

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using (var db = NewContext())
        {
            await db.Database.EnsureDeletedAsync();
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task Twenty_concurrent_claims_never_hand_the_same_job_to_two_instances()
    {
        const int jobs = 200;

        await SeedAsync(jobs);

        var now = DateTime.UtcNow;
        var claimed = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var (processor, context) = NewInstance(batchSize: 25);
            await using var db = context;
            return await processor.ClaimAsync(db, now, CancellationToken.None);
        }));

        var ids = claimed.SelectMany(batch => batch.Select(j => j.Id)).ToList();

        // The only invariant: no id claimed twice. See the note in PostgresClaimTests for why total coverage
        // in a single round is not one.
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEmpty(ids);
        Assert.True(ids.Count <= jobs, $"claimed {ids.Count} of {jobs} jobs — more than exist.");
    }

    [SqlServerFact]
    public async Task An_expired_lease_is_reclaimed_by_exactly_one_of_many_instances()
    {
        await SeedAsync(1);

        var now = DateTime.UtcNow;
        var (first, firstDb) = NewInstance(batchSize: 10);
        await using (firstDb)
        {
            Assert.Single(await first.ClaimAsync(firstDb, now, CancellationToken.None));
        }

        var afterExpiry = now + TimeSpan.FromMinutes(6);
        var reclaimed = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var (processor, context) = NewInstance(batchSize: 10);
            await using var db = context;
            return await processor.ClaimAsync(db, afterExpiry, CancellationToken.None);
        }));

        Assert.Equal(1, reclaimed.Sum(batch => batch.Count));
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

    private ProbeDbContext NewContext()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(SqlServer.Required)
        {
            InitialCatalog = _database,
        };

        return new ProbeDbContext(
            new DbContextOptionsBuilder<ProbeDbContext>().UseSqlServer(builder.ConnectionString).Options);
    }

    private (JobProcessor<ProbeDbContext> Processor, ProbeDbContext Context) NewInstance(int batchSize)
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
        return (processor, NewContext());
    }

}

[CollectionDefinition("sqlserver", DisableParallelization = true)]
public sealed class SqlServerCollection;
