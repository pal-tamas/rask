using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Outbox.Tests;

/// <summary>A save that failed leaves nothing of its events for the context's next save to write.</summary>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxSaveRetryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-retry-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;

    public OutboxSaveRetryTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Recorder());
        services.AddSingleton(new @event.KeywordRecorder());
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddRaskOutbox<OutboxDbContext>();
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_change_that_failed_to_save_never_reaches_a_durable_handler_through_a_later_save()
    {
        await using var db = NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER no_orders BEFORE INSERT ON Orders BEGIN SELECT RAISE(ABORT, 'refused'); END;",
            TestContext.Current.CancellationToken);
        var refused = Order.Place("ada");
        db.Orders.Add(refused);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        db.Entry(refused).State = EntityState.Detached; // the caller gives the change up
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER no_orders;", TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var read = NewContext();
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private OutboxDbContext NewContext() => _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();
}
