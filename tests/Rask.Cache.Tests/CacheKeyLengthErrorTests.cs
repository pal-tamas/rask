using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cache.Tests;

/// <summary>
///     A key the database cannot store is reported as a key that is too long, not as a provider error.
/// </summary>
/// <remarks>
///     PostgreSQL and SQL Server enforce the key column's length, so an over-long key fails the insert on truncation
///     and leaves no row for the upsert's update to find. SQLite never enforces the length, so these stand a failing
///     insert in for that server — the path under test is the cache's, not the provider's, and the real servers
///     prove the provider half in <c>Rask.Providers.E2E.Tests</c>.
/// </remarks>
[Collection(CacheDbCollection.Name)]
public sealed class CacheKeyLengthErrorTests
{
    private static readonly DistributedCacheEntryOptions FiveMinutes = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    [Fact]
    public async Task A_key_longer_than_the_mapped_column_is_rejected_with_the_limit_named()
    {
        await WithCacheAsync(async cache =>
        {
            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                cache.SetAsync(new string('k', 513), "stored"u8.ToArray(), FiveMinutes));

            Assert.StartsWith(
                "The cache key is 513 characters long, and this database's cache table holds keys of at most 512.",
                error.Message);
            Assert.Equal("key", error.ParamName);
            Assert.IsType<DbUpdateException>(error.InnerException);
        });
    }

    [Fact]
    public async Task A_failed_write_for_a_key_within_the_limit_still_surfaces_the_provider_error()
    {
        // The length explanation is for the case it explains. A key that fits and still fails is some other write
        // error, and must surface as itself.
        await WithCacheAsync(async cache =>
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                cache.SetAsync("short-key", "stored"u8.ToArray(), FiveMinutes)));
    }

    private static async Task WithCacheAsync(Func<IDistributedCache, Task> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rask-cache-key-length-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCache<CacheDbContext>();
        services.AddDbContextFactory<CacheDbContext>(o => o
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(new FailingInsertInterceptor()));

        await using var provider = services.BuildServiceProvider();

        try
        {
            await using (var db = await provider.GetRequiredService<IDbContextFactory<CacheDbContext>>().CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            await test(provider.GetRequiredService<IDistributedCache>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    // Stands in for a server refusing the insert: what PostgreSQL's 22001 and SQL Server's 2628 surface as.
    private sealed class FailingInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("simulated: value too long for the key column");
    }
}
