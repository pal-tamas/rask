using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Outbox.Tests;

// Inside Rask.* the bare word is the namespace; an app, outside it, writes `Outbox.Fake()` with no alias.
using Outbox = Rask.Cqrs.Outbox;

/// <summary>
/// The fake stands where the outbox table does: a durable handler's event is recorded rather than written,
/// and runs when the test says so.
/// </summary>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxFakeTests : IDisposable
{
    private readonly ShippingLog _log = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-outbox-fake-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public OutboxFakeTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddRaskCqrs();
        services.AddRaskOutbox<OutboxDbContext>();
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_path};Pooling=False")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        _provider = services.BuildServiceProvider();
        using var db = _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        File.Delete(_path);
    }

    [Fact]
    public async Task A_raised_event_is_recorded_instead_of_stored_as_a_row()
    {
        using var outbox = Outbox.Fake();

        await Save(new Shipped(Guid.NewGuid()));

        outbox.Stored<Shipped>().Once();
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task A_published_event_is_recorded_instead_of_stored_as_a_row()
    {
        using var outbox = Outbox.Fake();

        await _provider.GetRequiredService<IDispatcher>().Publish(new Shipped(Guid.NewGuid()), TestContext.Current.CancellationToken);

        outbox.Stored<Shipped>().Once();
        Assert.Empty(await Rows());
    }

    [Fact]
    public async Task The_in_memory_handler_still_runs_at_once()
    {
        using var outbox = Outbox.Fake();

        await Save(new Shipped(Guid.NewGuid()));

        Assert.Equal(["dashboard"], _log.Ran);
    }

    [Fact]
    public async Task Run_runs_each_durable_handler_once()
    {
        using var outbox = Outbox.Fake();
        await Save(new Shipped(Guid.NewGuid()));

        await outbox.Run(TestContext.Current.CancellationToken);
        await outbox.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["charge", "dashboard", "receipt"], _log.Ran.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_durable_handler_runs_as_the_user_who_raised_the_event()
    {
        var alice = Guid.NewGuid();
        using var outbox = Outbox.Fake();
        using (Current.UseUser(alice))
        {
            await Save(new Packed(Guid.NewGuid()));
        }

        using (Current.UseUser(Guid.NewGuid()))
        {
            await outbox.Run(TestContext.Current.CancellationToken);
        }

        Assert.Contains($"who:{alice}", _log.Ran);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_outbox_back()
    {
        using (Outbox.Fake())
        {
            await Save(new Shipped(Guid.NewGuid()));
        }

        await Save(new Shipped(Guid.NewGuid()));

        Assert.Equal(2, (await Rows()).Count);
    }

    private async Task Save(IEvent e)
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Orders.Add(Order.PlaceRaising(e));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<OutboxMessage>> Rows()
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await db.Set<OutboxMessage>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }
}
