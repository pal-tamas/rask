using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

// Inside Rask.* the bare word is the namespace; an app, outside it, writes `Outbox.Fake()` with no alias.
using Outbox = Rask.Cqrs.Outbox;

/// <summary>An app's own context whose model maps the outbox table, as a Rask app's does.</summary>
public sealed class ShopWithOutboxContext(DbContextOptions<ShopWithOutboxContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddRaskOutbox();
    }
}

/// <summary>A <c>TestDatabase</c> saves through the outbox as the app does, once its model maps the table.</summary>
public sealed class TestDatabaseOutboxTests : IDisposable
{
    private readonly ShippingLog _log = new();
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_saved_aggregate_s_durable_event_is_stored_in_the_save_and_its_in_memory_handler_runs()
    {
        await using var database = await Start();

        database.Context.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await database.Context.SaveChangesAsync(Ct);

        var handlers = await database.Context.Set<OutboxMessage>().Select(m => m.Handler).ToListAsync(Ct);
        Assert.Equal(2, handlers.Count);
        Assert.Equal(["dashboard"], _log.Ran);
    }

    [Fact]
    public async Task The_fake_outbox_records_what_a_test_database_save_stores_and_runs_it_when_asked()
    {
        await using var database = await Start();
        using var outbox = Outbox.Fake();

        database.Context.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await database.Context.SaveChangesAsync(Ct);
        await outbox.Run(Ct);

        outbox.Stored<Shipped>().Once();
        Assert.Equal(["charge", "dashboard", "receipt"], _log.Ran.Order(StringComparer.Ordinal));
        Assert.Equal(0, await database.Context.Set<OutboxMessage>().CountAsync(Ct));
    }

    private Task<TestDatabase<ShopWithOutboxContext>> Start() =>
        TestDatabase.Start<ShopWithOutboxContext>(
            o => o.UseSqlite(_db.ConnectionString),
            services: s => s.AddSingleton(_log).AddSingleton(new Recorder()),
            cancellationToken: Ct);
}
