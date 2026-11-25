using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

/// <summary>
///     A durable handler's row written inside <c>Db.Transaction</c> commits with the work and rolls back with it; the
///     in-memory handlers run after the commit.
/// </summary>
public sealed class DbTransactionOutboxTests : IAsyncDisposable
{
    private readonly InMemorySqlite _db = new();
    private readonly ShippingLog _log = new();
    private readonly ServiceProvider _provider;

    // The model surface pointed at this class's database for the test's flow alone: the constructor runs in the flow
    // its test does.
    private readonly IDisposable _facade;

    public DbTransactionOutboxTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddRaskCqrs();
        services.AddRaskOutbox<OutboxDbContext>();
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite(_db.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        _provider = services.BuildServiceProvider();

        var contexts = _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>();
        using var db = contexts.CreateDbContext();
        db.Database.EnsureCreated();
        _facade = Db.Use(contexts);
    }

    public async ValueTask DisposeAsync()
    {
        _facade.Dispose();
        await _provider.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task The_rows_commit_with_the_work_and_the_in_memory_handler_runs_after()
    {
        IReadOnlyList<string> ranBeforeCommit = ["not asked"];

        await Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Order.PlaceRaising(new Shipped(Guid.NewGuid())), cancellationToken: TestContext.Current.CancellationToken);
            ranBeforeCommit = _log.Ran;
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(ranBeforeCommit);
        Assert.Equal(["dashboard"], _log.Ran);
        Assert.Equal((1, 2), (await Count<Order>(), await Count<OutboxMessage>()));
    }

    [Fact]
    public async Task A_rollback_takes_the_rows_with_it()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await GeneratedModelWrites.Create(Order.PlaceRaising(new Shipped(Guid.NewGuid())), cancellationToken: TestContext.Current.CancellationToken);
            throw new InvalidOperationException("out of stock");
        }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(_log.Ran);
        Assert.Equal((0, 0), (await Count<Order>(), await Count<OutboxMessage>()));
    }

    private async Task<int> Count<T>()
        where T : class
    {
        await using var db = await _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await db.Set<T>().IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken);
    }
}
