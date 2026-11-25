using Microsoft.EntityFrameworkCore;
using Rask.Data;
using Rask.Tests.Shared;

namespace Rask.Caching.Tests;

/// <summary>An app's context, mapping the cache table as a RaskApp's does.</summary>
public sealed class ShelfContext(DbContextOptions<ShelfContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskCache();
    }
}

/// <summary>A <c>TestDatabase</c> over a model that maps the cache table stands the cache in with its fake.</summary>
public sealed class CacheTestDatabaseTests : IDisposable
{
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_remember_with_no_host_is_served_by_the_database_s_cache_fake()
    {
        await using var database = await TestDatabase.Start<ShelfContext>(o => o.UseSqlite(_db.ConnectionString), cancellationToken: Ct);
        var loads = 0;

        await Cache.Remember("products", () => ++loads, Ct).For(10.Minutes);
        await Cache.Remember("products", () => ++loads, Ct).For(10.Minutes);

        database.Cache.Loaded("products").Once();
    }
}
