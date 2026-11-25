using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Outbox.Tests;

// Inside Rask.* the bare word is the namespace; an app, outside it, writes `Outbox.Fake()` with no alias.
using Outbox = Rask.Cqrs.Outbox;

/// <summary>
///     The fake records what the outbox would have COMMITTED: an event whose save failed or whose transaction rolled
///     back never reaches a durable handler for real, so the fake does not say it was stored either.
/// </summary>
public sealed class OutboxFakeFailedSaveTests : IAsyncLifetime
{
    private readonly SqliteConnection _keeper = new($"Data Source=rask-outbox-fake-failed-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private ServiceProvider _provider = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _keeper.OpenAsync(Ct);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ShippingLog());
        services.AddRaskCqrs();
        services.AddRaskOutbox<OutboxDbContext>();
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite(_keeper.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        _provider = services.BuildServiceProvider();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keeper.DisposeAsync();
    }

    [Fact]
    public async Task An_event_whose_save_the_database_refused_is_not_recorded()
    {
        using var outbox = Outbox.Fake();
        await using var db = NewContext();
        await RefuseOrders(db);

        db.Orders.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

        outbox.Stored<Shipped>().None();
    }

    [Fact]
    public async Task A_save_retried_after_it_failed_records_its_event_once()
    {
        using var outbox = Outbox.Fake();
        await using var db = NewContext();
        await RefuseOrders(db);
        db.Orders.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER no_orders;", Ct);

        await db.SaveChangesAsync(Ct);

        outbox.Stored<Shipped>().Once();
    }

    [Fact]
    public async Task An_event_whose_transaction_rolled_back_is_not_recorded()
    {
        using var outbox = Outbox.Fake();
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);

        db.Orders.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await db.SaveChangesAsync(Ct);
        await transaction.RollbackAsync(Ct);

        outbox.Stored<Shipped>().None();
    }

    [Fact]
    public async Task An_event_whose_transaction_committed_is_recorded()
    {
        using var outbox = Outbox.Fake();
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);

        db.Orders.Add(Order.PlaceRaising(new Shipped(Guid.NewGuid())));
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);

        outbox.Stored<Shipped>().Once();
    }

    private static Task RefuseOrders(OutboxDbContext db) =>
        db.Database.ExecuteSqlRawAsync("CREATE TRIGGER no_orders BEFORE INSERT ON Orders BEGIN SELECT RAISE(ABORT, 'refused'); END;", Ct);

    private OutboxDbContext NewContext() => _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();
}
