using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Cache;
using Rask.SqlServer;

namespace Rask.Providers.E2E.Tests;

public sealed class SqlClaimDbContext(DbContextOptions<SqlClaimDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

public sealed class SqlCacheDbContext(DbContextOptions<SqlCacheDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskCache();
}

public sealed class SqlBulkDbContext(DbContextOptions<SqlBulkDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>().ToTable("Order");
}

/// <summary>
/// The jobs claim against a real SQL Server, through <c>UseRaskSqlServer</c> and its retrying strategy.
/// </summary>
/// <remarks>
/// Same purpose as <see cref="PostgresClaimTests"/>, different engine: under both locking READ COMMITTED and
/// RCSI, a data-modification statement takes update locks, blocks on a row another transaction is changing, and
/// re-reads it before applying its own predicate — so the loser skips the row.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerClaimTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_claim";
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        if (SqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SkippableFact]
    public async Task Twenty_concurrent_claims_never_hand_the_same_job_to_two_instances()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        const int jobs = 200;
        await SeedAsync(jobs);

        var claimed = await ClaimTogetherAsync(instances: 20, batchSize: 25, DateTime.UtcNow);

        var ids = claimed.SelectMany(batch => batch.Select(j => j.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.NotEmpty(ids);
        Assert.True(ids.Count <= jobs, $"claimed {ids.Count} of {jobs} jobs — more than exist.");
    }

    [SkippableFact]
    public async Task An_expired_lease_is_reclaimed_by_exactly_one_of_many_instances()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        await SeedAsync(1);

        var now = DateTime.UtcNow;
        var (first, firstDb) = NewInstance(batchSize: 10);
        await using (firstDb)
        {
            Assert.Single(await first.ClaimAsync(firstDb, now, CancellationToken.None));
        }

        var reclaimed = await ClaimTogetherAsync(instances: 10, batchSize: 10, now + TimeSpan.FromMinutes(6));

        Assert.Equal(1, reclaimed.Sum(batch => batch.Count));
    }

    [SkippableFact]
    public async Task The_largest_allowed_batch_claims_in_one_statement()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        // SQL Server's ceiling is 2,100 parameters per statement; EF Core 10 pads the claim's id list into
        // parameters, so the 1000-row BatchSize cap is only safe if the widest list fits.
        await SeedAsync(1000);

        var (processor, context) = NewInstance(batchSize: 1000);
        await using (context)
        {
            var batch = await processor.ClaimAsync(context, DateTime.UtcNow, CancellationToken.None);

            Assert.Equal(1000, batch.Count);
            Assert.Equal(1000, batch.Select(j => j.Id).Distinct().Count());
        }
    }

    private async Task<List<Job>[]> ClaimTogetherAsync(int instances, int batchSize, DateTime now)
    {
        // Built first, released together — see PostgresClaimTests.ClaimTogetherAsync.
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

    private static async Task SeedAsync(int count)
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

    private static SqlClaimDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqlClaimDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);

    private (JobProcessor<SqlClaimDbContext> Processor, SqlClaimDbContext Context) NewInstance(int batchSize)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskJobs<SqlClaimDbContext>(o =>
        {
            o.BatchSize = batchSize;
            o.LeaseDuration = TimeSpan.FromMinutes(5);
        });
        services.AddDbContextFactory<SqlClaimDbContext>(o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)));

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var processor = provider.GetServices<IHostedService>().OfType<JobProcessor<SqlClaimDbContext>>().Single();
        return (processor, NewContext());
    }
}

/// <summary>
/// <c>XACT_ABORT</c> and <c>LOCK_TIMEOUT</c> reach the session, and are re-applied after the pool resets it.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSessionSettingsTests
{
    [SkippableFact]
    public async Task Every_connection_EF_opens_carries_the_configured_settings()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        var options = new DbContextOptionsBuilder<SqlClaimDbContext>()
            .UseRaskSqlServerAt(SqlServer.ConnectionString!, o =>
            {
                o.CommandTimeout = TimeSpan.FromSeconds(30);
                o.LockTimeout = TimeSpan.FromSeconds(7);
            })
            .Options;

        // The second pass takes the same physical connection back out of the pool, after sp_reset_connection.
        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = new SqlClaimDbContext(options);
            await db.Database.OpenConnectionAsync();
            var connection = db.Database.GetDbConnection();

            Assert.Equal(7000, await ScalarAsync(connection, "SELECT @@LOCK_TIMEOUT"));
            Assert.Equal(16384, await ScalarAsync(connection, "SELECT CAST(@@OPTIONS & 16384 AS int)"));

            // Undo both inside the session: the next open must put them back.
            await ScalarAsync(connection, "SET XACT_ABORT OFF; SET LOCK_TIMEOUT -1; SELECT 0");
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<int> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>The cache on SQL Server: a key past 450 characters, and many writers on one cold key.</summary>
/// <remarks>
/// Without <c>UseRaskSqlServer</c>'s key cap the table is created with an <c>nvarchar(512)</c> clustered key,
/// which SQL Server accepts with a warning and then refuses to insert a long key into.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerCacheTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_cache";
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<SqlCacheDbContext>(o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)));
        services.AddRaskCache<SqlCacheDbContext>();
        _provider = services.BuildServiceProvider();

        await using var db = await NewContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is null)
        {
            return;
        }

        await using (var db = await NewContextAsync())
        {
            await db.Database.EnsureDeletedAsync();
        }

        await _provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task The_key_column_is_created_within_the_index_key_limit()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        // Asserted on the created column, not by a round trip: a 450-character key fits nvarchar(512) too, so a
        // round trip passes with the convention deleted.
        await using var db = await NewContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CacheEntry' AND COLUMN_NAME = 'Key'";

        Assert.Equal(450, Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [SkippableFact]
    public async Task A_key_of_the_largest_allowed_length_round_trips()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        var cache = _provider!.GetRequiredService<IDistributedCache>();
        var key = new string('k', 450);

        await cache.SetAsync(key, "stored"u8.ToArray(), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        });

        Assert.Equal("stored", Encoding.UTF8.GetString((await cache.GetAsync(key))!));
    }

    [SkippableFact]
    public async Task A_longer_key_is_rejected_with_the_limit_named()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        var cache = _provider!.GetRequiredService<IDistributedCache>();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => cache.SetAsync(
            new string('k', 451),
            "stored"u8.ToArray(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }));

        Assert.StartsWith("The cache key is 451 characters long, and this database's cache table holds keys of at most 450.", error.Message);
    }

    [SkippableFact]
    public async Task Fifty_concurrent_writers_on_one_cold_key_all_succeed()
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        var cache = _provider!.GetRequiredService<IDistributedCache>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = Enumerable.Range(0, 50).Select(i => $"value-{i}").ToArray();

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

        var stored = await cache.GetAsync("cold-key");
        Assert.NotNull(stored);
        Assert.Contains(Encoding.UTF8.GetString(stored), values);
    }

    private Task<SqlCacheDbContext> NewContextAsync() =>
        _provider!.GetRequiredService<IDbContextFactory<SqlCacheDbContext>>().CreateDbContextAsync();
}

/// <summary>Bulk insert into a keyword-named table, bracket-quoted by SQL Server's own generation helper.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerBulkInsertTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_bulk";

    public async Task InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ten_thousand_rows_land_in_a_keyword_named_table(bool singleTransaction)
    {
        Skip.IfNot(SqlServer.Available, SqlServer.SkipReason);

        var orders = Enumerable.Range(0, 10_000)
            .Select(i => new Order { Id = Guid.NewGuid(), Group = $"g{i % 7}", Select = i })
            .ToList();

        await using (var db = NewContext())
        {
            var written = await db.BulkInsertAsync(orders, o =>
            {
                o.SkipChangeTracking = true;
                o.SingleTransaction = singleTransaction;
            });

            Assert.Equal(10_000, written);
        }

        await using var verify = NewContext();
        Assert.Equal(10_000, await verify.Orders.CountAsync());
        Assert.Equal(orders.Sum(o => (long)o.Select), await verify.Orders.SumAsync(o => (long)o.Select));
    }

    private static SqlBulkDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqlBulkDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);
}
