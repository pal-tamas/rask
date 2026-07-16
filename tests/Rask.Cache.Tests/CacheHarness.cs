using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Cache.Tests;

public sealed class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskCache();
}

/// <summary>A hand-rolled fake clock (no external package): the cache's expiry checks read it, so tests drive
/// time deterministically.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Builds a real-SQLite service provider wired for the cache, with a controllable clock.</summary>
public sealed class CacheHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public CacheHarness(Action<CacheOptions>? configure = null, DateTimeOffset? start = null)
    {
        DbPath = Path.Combine(Path.GetTempPath(), $"rask-cache-test-{Guid.NewGuid():N}.db");
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock); // registered first so AddRaskCache' TryAddSingleton keeps it
        services.AddRaskCache<CacheDbContext>(o =>
        {
            o.PurgeInterval = TimeSpan.FromMilliseconds(20);
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<CacheDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public string DbPath { get; }

    public FakeTimeProvider Clock { get; }

    public IDistributedCache Distributed => _provider.GetRequiredService<IDistributedCache>();

    public ICache Cache => _provider.GetRequiredService<ICache>();

    public IHostedService Purger =>
        _provider.GetServices<IHostedService>().OfType<CachePurger<CacheDbContext>>().Single();

    public CacheDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<CacheDbContext>>().CreateDbContext();

    public async Task<int> CountEntriesAsync()
    {
        await using var db = NewContext();
        return await db.Set<CacheEntry>().CountAsync();
    }

    public async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }
    }
}
