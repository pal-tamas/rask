using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

/// <summary>
/// The drain persists each message's outcome on its own, so a row changing underneath the batch costs at most
/// that one row. Before this, the whole batch was written by a single <c>SaveChangesAsync</c> at the end: one
/// concurrently deleted row raised <see cref="DbUpdateConcurrencyException" />, rolled the transaction back, and
/// stripped <c>ProcessedAt</c> from every message already published in that batch — so they were all published
/// again on the next poll. Duplicate delivery is the exact failure the outbox exists to prevent.
/// </summary>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxConcurrencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-conc-{Guid.NewGuid():N}.db");
    private readonly Recorder _recorder = new();
    private readonly ServiceProvider _provider;

    public OutboxConcurrencyTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_recorder);
        services.AddSingleton(new @event.KeywordRecorder());
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
        services.AddRaskOutbox<OutboxDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(50));
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task A_row_deleted_mid_batch_does_not_republish_the_rest()
    {
        // Saved one at a time so the outbox ids ascend in a known order: the saboteur is drained first and
        // deletes the last row while the batch containing it is still in flight.
        await SaveAsync(Order.PlaceRaisingSaboteur());
        await SaveAsync(Order.Place("b"));
        await SaveAsync(Order.Place("c"));

        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            // The doomed row is gone and the two survivors are marked published. Under the old batch-wide save
            // this never settles: the survivors keep losing ProcessedAt and re-publishing on every poll.
            await WaitUntilAsync(async () =>
            {
                await using var poll = NewContext();
                return await poll.Set<OutboxMessage>().CountAsync() == 2
                       && await poll.Set<OutboxMessage>().AllAsync(m => m.ProcessedAt != null);
            });

            // A few more poll cycles, to prove the state is stable rather than momentary.
            await Task.Delay(300);
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        await using var read = NewContext();
        var messages = await read.Set<OutboxMessage>().OrderBy(m => m.Id).ToListAsync();

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.NotNull(m.ProcessedAt));
        // One attempt started each, none of them a retry — Attempts counts claims, not failures.
        Assert.All(messages, m => Assert.Equal(1, m.Attempts));

        // The real payoff: each event was delivered exactly once.
        Assert.Equal(1, _recorder.Events.Count(e => e.Customer == "b"));
        Assert.Equal(1, _recorder.Events.Count(e => e.Customer == "c"));
    }

    [Fact]
    public async Task A_faulting_cycle_does_not_stop_the_processor()
    {
        await SaveAsync(Order.PlaceRaisingSaboteur());
        await SaveAsync(Order.Place("first"));

        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            // The saboteur deletes "first" mid-drain, so that row's save throws out of the drain. Without the
            // per-cycle guard that exception faults the BackgroundService — and with the default
            // BackgroundServiceExceptionBehavior.StopHost, takes the whole application down with it.
            await WaitUntilAsync(async () =>
            {
                await using var poll = NewContext();
                return await poll.Set<OutboxMessage>().CountAsync() == 1;
            });

            // The loop must still be alive: an event raised after the fault is still delivered.
            await SaveAsync(Order.Place("after-the-fault"));
            await WaitUntilAsync(() => Task.FromResult(_recorder.Events.Any(e => e.Customer == "after-the-fault")));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains(_recorder.Events, e => e.Customer == "after-the-fault");
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private OutboxDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    private async Task SaveAsync(Order order)
    {
        await using var db = NewContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(20);
        }
    }
}
