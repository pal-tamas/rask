using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

public sealed record Dispatched(Guid Id) : IEvent;

public sealed record Parceled(Guid Id) : IEvent;

/// <summary>An event nothing handles, so the handler's own write stores no row.</summary>
public sealed record Unheard(Guid Id) : IEvent;

/// <summary>What the dispatch handler saw, and a switch that makes it fail after its write.</summary>
public sealed class DispatchLog
{
    public bool FailsAfterWriting { get; set; }

    public ConcurrentQueue<(long MessageId, int Attempt)> Seen { get; } = new();
}

/// <summary>Writes a row of its own, then — when told to — fails.</summary>
public sealed class RecordDispatch(IDbContextFactory<OutboxDbContext> contexts, DispatchLog log) : IDurableHandler<Dispatched>
{
    public async Task Handle(Dispatched e)
    {
        if (DurableEvent.Current is { } running)
        {
            log.Seen.Enqueue((running.MessageId, running.Attempt));
        }

        await using var db = await contexts.CreateDbContextAsync();
        db.Orders.Add(Order.PlaceRaising(new Unheard(e.Id)));
        await db.SaveChangesAsync();

        if (log.FailsAfterWriting)
        {
            throw new InvalidOperationException("the courier refused it");
        }
    }
}

/// <summary>An aggregate whose <c>static Task On(E e)</c> the generator makes a durable handler of.</summary>
public sealed partial class Consignment : Aggregate<Guid>
{
    internal static ConcurrentQueue<Guid> Handled { get; } = new();

    private Consignment() { }

    static Task On(Parceled e)
    {
        Handled.Enqueue(e.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
///     F-21: <c>OutboxOptions.HandlerInTransaction</c> — a durable handler's writes and the mark that its row is done commit
///     together or not at all; <c>DurableEvent.Current</c> names the row; and an aggregate's <c>static Task On(E e)</c> is a
///     durable handler of its own, run by the outbox.
/// </summary>
[Collection(OutboxDbCollection.Name)]
public sealed class HandlerInTransactionTests : IDisposable
{
    private readonly DispatchLog _log = new();
    private readonly List<string> _paths = [];
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        foreach (var path in _paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A test file left behind is not a failure worth failing the run for.
            }
        }
    }

    [Fact]
    public async Task In_the_transaction_the_handlers_write_and_the_mark_commit_together()
    {
        var provider = Build(handlerInTransaction: true);
        await Save(provider, new Dispatched(Guid.NewGuid()));

        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await Dispatches(provider));
        Assert.NotNull(Assert.Single(await Rows(provider)).ProcessedAt);
    }

    [Fact]
    public async Task In_the_transaction_a_handler_that_fails_after_writing_leaves_neither_its_write_nor_the_mark()
    {
        var provider = Build(handlerInTransaction: true);
        _log.FailsAfterWriting = true;
        await Save(provider, new Dispatched(Guid.NewGuid()));

        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(await Rows(provider));
        Assert.Equal(0, await Dispatches(provider));
        Assert.Null(row.ProcessedAt);
        Assert.Equal("the courier refused it", row.Error);
        Assert.Null(row.ClaimToken);
    }

    [Fact]
    public async Task Outside_the_transaction_a_handler_that_fails_after_writing_keeps_its_write_for_the_retry_to_meet()
    {
        var provider = Build(handlerInTransaction: false);
        _log.FailsAfterWriting = true;
        await Save(provider, new Dispatched(Guid.NewGuid()));

        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, await Dispatches(provider));
        Assert.Null(Assert.Single(await Rows(provider)).ProcessedAt);
    }

    [Fact]
    public async Task The_handler_reads_which_stored_row_and_attempt_it_runs_for()
    {
        var provider = Build(handlerInTransaction: true);
        _log.FailsAfterWriting = true;
        await Save(provider, new Dispatched(Guid.NewGuid()));
        var id = Assert.Single(await Rows(provider)).Id;

        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);
        _log.FailsAfterWriting = false;
        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(id, 1), (id, 2)], _log.Seen);
        Assert.Null(DurableEvent.Current);
    }

    [Fact]
    public async Task An_aggregates_On_is_stored_as_its_own_durable_handler_and_run_by_the_outbox()
    {
        var provider = Build(handlerInTransaction: true);
        var parcel = Guid.NewGuid();
        await Save(provider, new Parceled(parcel));

        var stored = Assert.Single(await Rows(provider));
        await Processor(provider).RunCycleAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"{CqrsRegistry.NameOf(typeof(Consignment))}:{CqrsRegistry.NameOf(typeof(Parceled))}", stored.Handler);
        Assert.Contains(parcel, Consignment.Handled);
        Assert.NotNull(Assert.Single(await Rows(provider)).ProcessedAt);
    }

    private ServiceProvider Build(bool handlerInTransaction)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-outbox-in-transaction-{Guid.NewGuid():N}.db");
        _paths.Add(path);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddRaskCqrs();
        services.AddRaskOutbox<OutboxDbContext>(o => o.HandlerInTransaction = handlerInTransaction);
        services.AddRaskData<OutboxDbContext>();
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={path}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        using var db = provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        return provider;
    }

    private static OutboxProcessor<OutboxDbContext> Processor(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();

    private static async Task Save(ServiceProvider provider, IEvent e)
    {
        await using var db = await provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContextAsync();
        db.Orders.Add(Order.PlaceRaising(e));
        await db.SaveChangesAsync();
    }

    private static async Task<List<OutboxMessage>> Rows(ServiceProvider provider)
    {
        await using var db = await provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContextAsync();
        return await db.Set<OutboxMessage>().AsNoTracking().ToListAsync();
    }

    private static async Task<int> Dispatches(ServiceProvider provider)
    {
        await using var db = await provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContextAsync();
        // Every order but the one the test saved to raise the event.
        return await db.Orders.CountAsync() - 1;
    }
}
