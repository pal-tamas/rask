using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Cache;
using Rask.MySql;

namespace Rask.Providers.E2E.Tests;

public sealed class MyClaimDbContext(DbContextOptions<MyClaimDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

public sealed class MyCacheDbContext(DbContextOptions<MyCacheDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskCache();
}

public sealed class MyBulkDbContext(DbContextOptions<MyBulkDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>().ToTable("Order");
}

/// <summary>The value shapes the batteries store: a client-assigned Guid, a DateTimeOffset and a UTC DateTime.</summary>
public sealed class Stamp
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    public DateTime When { get; set; }
}

public sealed class MyTypesDbContext(DbContextOptions<MyTypesDbContext> options) : DbContext(options)
{
    public DbSet<Stamp> Stamps => Set<Stamp>();
}

/// <summary>The jobs claim against a real MySQL, through <c>UseRaskMySql</c> and its retrying strategy.</summary>
/// <remarks>
/// Under InnoDB's default REPEATABLE READ, an <c>UPDATE</c> takes a current read with row locks rather than the
/// transaction's snapshot, so the losing instance blocks on the winner's rows and re-tests the predicate against the
/// committed version — the same property the design relies on elsewhere.
/// </remarks>
[Collection(MySqlCollection.Name)]
public sealed class MySqlClaimTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_claim";
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        if (MySqlServer.Available)
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

        if (MySqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SkippableFact]
    public async Task Twenty_concurrent_claims_never_hand_the_same_job_to_two_instances()
    {
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

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
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

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
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

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

    private static MyClaimDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MyClaimDbContext>().UseRaskMySql(MySqlServer.Database(DatabaseName)).Options);

    private (JobProcessor<MyClaimDbContext> Processor, MyClaimDbContext Context) NewInstance(int batchSize)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskJobs<MyClaimDbContext>(o =>
        {
            o.BatchSize = batchSize;
            o.LeaseDuration = TimeSpan.FromMinutes(5);
        });
        services.AddDbContextFactory<MyClaimDbContext>(o => o.UseRaskMySql(MySqlServer.Database(DatabaseName)));

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var processor = provider.GetServices<IHostedService>().OfType<JobProcessor<MyClaimDbContext>>().Single();
        return (processor, NewContext());
    }
}

/// <summary>
/// The two timeouts reach the session, and are put back on the next open even when the session changed them.
/// </summary>
/// <remarks>
/// The driver's pool does not reset a session by default, so a value changed inside one checkout would otherwise
/// leak into the next — this pins that every EF open re-applies the configured values.
/// </remarks>
[Collection(MySqlCollection.Name)]
public sealed class MySqlSessionSettingsTests
{
    [SkippableFact]
    public async Task Every_connection_EF_opens_carries_the_configured_settings()
    {
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

        var options = new DbContextOptionsBuilder<MyClaimDbContext>()
            .UseRaskMySql(MySqlServer.ConnectionString!, o =>
            {
                o.LockTimeout = TimeSpan.FromSeconds(7);
                o.StatementTimeout = TimeSpan.FromSeconds(42);
            })
            .Options;

        long firstSession = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            await using var db = new MyClaimDbContext(options);
            await db.Database.OpenConnectionAsync();
            var connection = db.Database.GetDbConnection();

            // The second pass must reuse the first pass's server session; a fresh one starts from the server's
            // defaults and would pass without proving the settings are re-applied.
            var session = await ScalarAsync(connection, "SELECT CONNECTION_ID()");
            if (pass == 0)
            {
                firstSession = session;
            }
            else
            {
                Assert.Equal(firstSession, session);
            }

            Assert.Equal(7, await ScalarAsync(connection, "SELECT @@SESSION.innodb_lock_wait_timeout"));
            Assert.Equal(42000, await ScalarAsync(connection, "SELECT @@SESSION.max_execution_time"));

            // Undo both inside the session: the next open must put them back.
            await ExecuteAsync(connection, "SET SESSION innodb_lock_wait_timeout = 50, SESSION max_execution_time = 0");
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<long> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>The cache on MySQL: many writers on one cold key, and a key longer than the column.</summary>
[Collection(MySqlCollection.Name)]
public sealed class MySqlCacheTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_cache";
    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        if (!MySqlServer.Available)
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<MyCacheDbContext>(o => o.UseRaskMySql(MySqlServer.Database(DatabaseName)));
        services.AddRaskCache<MyCacheDbContext>();
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
    public async Task Fifty_concurrent_writers_on_one_cold_key_all_succeed()
    {
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

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

    [SkippableFact]
    public async Task A_key_longer_than_the_column_is_rejected_with_the_limit_named()
    {
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

        var cache = _provider!.GetRequiredService<IDistributedCache>();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => cache.SetAsync(
            new string('k', 513),
            Encoding.UTF8.GetBytes("stored"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }));

        Assert.StartsWith("The cache key is 513 characters long, and this database's cache table holds keys of at most 512.", error.Message);
    }

    private Task<MyCacheDbContext> NewContextAsync() =>
        _provider!.GetRequiredService<IDbContextFactory<MyCacheDbContext>>().CreateDbContextAsync();
}

/// <summary>Bulk insert into a keyword-named table, which MySQL rejects unless it is backtick-quoted.</summary>
[Collection(MySqlCollection.Name)]
public sealed class MySqlBulkInsertTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_bulk";

    public async Task InitializeAsync()
    {
        if (MySqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (MySqlServer.Available)
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
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

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

    private static MyBulkDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MyBulkDbContext>().UseRaskMySql(MySqlServer.Database(DatabaseName)).Options);
}

/// <summary>
/// The value shapes the batteries store, round-tripped through Oracle's provider on a server whose own time zone is
/// not UTC.
/// </summary>
/// <remarks>
/// These are the verification items that could not be settled offline: whether a DateTimeOffset comes back as the
/// same instant (the provider has a record of shifting it by the server's offset), whether a DateTime keeps
/// sub-second precision, and how a client-assigned Guid is stored. The gate starts the server at +05:00 so a shift
/// cannot hide behind a UTC server.
/// </remarks>
[Collection(MySqlCollection.Name)]
public sealed class MySqlTypeRoundTripTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_e2e_types";

    public async Task InitializeAsync()
    {
        if (MySqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (MySqlServer.Available)
        {
            await using var db = NewContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SkippableFact]
    public async Task A_guid_a_date_time_offset_and_a_utc_date_time_come_back_unchanged()
    {
        Skip.IfNot(MySqlServer.Available, MySqlServer.SkipReason);

        // One value at UTC and one at +02:00: a provider that writes the local wall clock without converting only
        // shows up on the second.
        var stamps = new[] { TimeSpan.Zero, TimeSpan.FromHours(2) }.Select(offset => new Stamp
        {
            Id = Guid.NewGuid(),
            At = new DateTimeOffset(2026, 3, 4, 5, 6, 7, offset).AddTicks(1_234_560),
            When = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_560),
        }).ToList();

        await using (var db = NewContext())
        {
            db.Stamps.AddRange(stamps);
            await db.SaveChangesAsync();
        }

        await using var verify = NewContext();
        foreach (var stamp in stamps)
        {
            var stored = await verify.Stamps.SingleAsync(s => s.Id == stamp.Id);

            Assert.Equal(stamp.Id, stored.Id);
            Assert.Equal(stamp.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), stored.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            Assert.Equal(stamp.When.Ticks, stored.When.Ticks);

            // Compared on the server against the exact value written — so the parameter goes through the same
            // conversion as the column, +02:00 included.
            Assert.True(await verify.Stamps.AnyAsync(s => s.Id == stamp.Id && s.At == stamp.At));
        }
    }

    private static MyTypesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<MyTypesDbContext>().UseRaskMySql(MySqlServer.Database(DatabaseName)).Options);
}
