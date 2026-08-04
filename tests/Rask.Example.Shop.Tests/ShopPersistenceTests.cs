using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;
using Rask.Example.Shop.Features.Orders;
using Rask.Example.Shop.Features.Products;
using Rask.Example.Shop.Features.Shared;
using Rask.Outbox;
using Rask.SQLite;

namespace Rask.Example.Shop.Tests;

/// <summary>
/// The sample's data layer against a real SQLite file — the behaviour <c>Rask.Data</c> adds to every
/// aggregate, and the transactional guarantee <c>Rask.Outbox</c> adds on top.
/// </summary>
/// <remarks>
/// Built with the same registrations as the app's <c>Program.cs</c>, in the same order, because that
/// order is what makes the behaviour correct.
/// </remarks>
public sealed class ShopPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-shop-test-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public ShopPersistenceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
        services.AddRaskOutbox<AppDbContext>();
        services.AddDbContextFactory<AppDbContext>((sp, o) => o
            .UseRaskSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private AppDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();

    [Fact]
    public void Every_pillar_gets_its_table()
    {
        // Each AddRaskX() in OnModelCreating maps one pillar's storage. A missing call compiles fine and
        // then faults the pillar's hosted service on its first poll, which stops the host.
        using var db = NewContext();
        var tables = db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToList();

        Assert.Contains("Products", tables);
        Assert.Contains("Orders", tables);
        Assert.Contains("OutboxMessage", tables);
        Assert.Contains("Job", tables);
        Assert.Contains("QueuedMail", tables);
        Assert.Contains("CacheEntry", tables);
    }

    [Fact]
    public async Task Creating_an_order_writes_its_event_in_the_same_save()
    {
        // The transactional guarantee: the order row and the outbox row commit together, so there is no
        // window in which an order exists but its confirmation was never scheduled.
        await using (var db = NewContext())
        {
            db.Orders.Add(Order.Create("Ada", 19.99m));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var message = await read.Set<OutboxMessage>().SingleAsync();

        Assert.Contains("OrderCreated", message.Type, StringComparison.Ordinal);
        Assert.Null(message.ProcessedAt); // written, not yet relayed
        Assert.Equal(1, await read.Orders.CountAsync());
    }

    [Fact]
    public async Task The_stored_event_name_is_one_the_runtime_can_resolve()
    {
        // A name the registry doesn't know doesn't throw — it fails to deserialize, and the message
        // retries until it dead-letters. Cheap tripwire for a missing analyzer reference or a naming
        // regression in the outbox generator.
        await using (var db = NewContext())
        {
            db.Orders.Add(Order.Create("Grace", 5m));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var message = await read.Set<OutboxMessage>().SingleAsync();

        Assert.DoesNotContain('@', message.Type);
        Assert.DoesNotContain('+', message.Type);
        Assert.NotNull(OutboxSerializerRegistry.Deserialize(message.Type, message.Payload));
    }

    [Fact]
    public async Task Auditing_stamps_created_and_moves_updated()
    {
        Guid id;
        DateTime created;
        await using (var db = NewContext())
        {
            var product = Product.Create("Espresso beans", 12.50m, inStock: true);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            (id, created) = (product.Id, product.CreatedAt);
        }

        Assert.NotEqual(default, created);

        await using (var db = NewContext())
        {
            var product = await db.Products.SingleAsync(p => p.Id == id);
            product.Update("Espresso beans (dark)", 13.50m, inStock: true);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var updated = await read.Products.SingleAsync(p => p.Id == id);
        Assert.Equal(created, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created, "UpdatedAt should have moved to the edit.");
    }

    [Fact]
    public async Task Deleting_soft_deletes_and_the_row_stays_behind_the_filter()
    {
        // Asserted behaviourally rather than through EF's metadata APIs: the shape of those has moved
        // between versions, and what actually matters is that a plain query can't see it.
        Guid id;
        await using (var db = NewContext())
        {
            var product = Product.Create("Filter papers", 4m, inStock: true);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;

            db.Products.Remove(product);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        Assert.Empty(await read.Products.Where(p => p.Id == id).ToListAsync());

        var deleted = await read.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        Assert.NotNull(deleted.DeletedAt); // soft, not gone
    }

    [Fact]
    public async Task A_stale_save_is_rejected_as_a_concurrency_conflict()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var product = Product.Create("Cold brew kit", 29.95m, inStock: false);
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        }

        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Products.SingleAsync(p => p.Id == id);
        var b = await second.Products.SingleAsync(p => p.Id == id);

        a.Update("Cold brew kit", 24.95m, inStock: true);
        await first.SaveChangesAsync();

        b.Update("Cold brew kit", 19.95m, inStock: true);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
