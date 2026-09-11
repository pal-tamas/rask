using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cache;

namespace Rask.Providers.E2E.Tests;

public sealed class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_e2e_cache";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddRaskCache();
    }
}

/// <summary>
/// The database-backed cache's upsert, on a server that aborts a transaction at its first error.
/// </summary>
/// <remarks>
/// <c>RaskDistributedCache</c> inserts, catches the duplicate-key failure, and updates instead. On SQLite a
/// failed statement leaves the connection usable; PostgreSQL aborts the transaction it ran in, so the update
/// has to run in a fresh one. Many writers on one cold key is the shape that exercises it.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresCacheTests : IAsyncLifetime
{
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        if (!Postgres.Available)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<CacheDbContext>(o => o.UseRaskPostgresAt(Postgres.Required));
        services.AddRaskCache<CacheDbContext>();
        _provider = services.BuildServiceProvider();

        await using var db = await NewContextAsync();
        await Postgres.ResetSchemaAsync(db, CacheDbContext.Schema);
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        await using (var db = await NewContextAsync())
        {
            await Postgres.DropSchemaAsync(db, CacheDbContext.Schema);
        }

        await _provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task Fifty_concurrent_writers_on_one_cold_key_all_succeed()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        var cache = _provider!.GetRequiredService<IDistributedCache>();
        const int writers = 50;

        // An awaited gate, not a Barrier: a Barrier parks one thread-pool thread per writer, and the pool injects
        // threads past its minimum at about one a second — fifty writers spent twenty seconds waiting for threads
        // rather than for the database, and arrived staggered rather than together.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = Enumerable.Range(0, writers).Select(i => $"value-{i}").ToArray();
        var sets = values.Select(async value =>
        {
            await start.Task;
            await cache.SetAsync("cold-key", Encoding.UTF8.GetBytes(value), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            });
        }).ToList();

        start.SetResult();
        await Task.WhenAll(sets);

        // Last writer wins, and which one that is is interleaving — but it is one of them, intact.
        var stored = await cache.GetAsync("cold-key");
        Assert.NotNull(stored);
        Assert.Contains(Encoding.UTF8.GetString(stored), values);
    }

    private Task<CacheDbContext> NewContextAsync() =>
        _provider!.GetRequiredService<IDbContextFactory<CacheDbContext>>().CreateDbContextAsync();
}
