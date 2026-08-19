using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

// An entity that raises an outbox event on creation.
public sealed class Order : Entity<Guid>
{
    private Order() { }

    public string Customer { get; private set; } = "";

    public static Order Place(string customer)
    {
        var order = new Order { Id = Guid.NewGuid(), Customer = customer };
        order.Raise(new OrderPlaced(order.Id, customer));
        return order;
    }

    // Raises an event declared in a keyword-named namespace — see KeywordNamespaceEvent.cs.
    public static Order PlaceRaisingKeywordEvent(string customer)
    {
        var order = new Order { Id = Guid.NewGuid(), Customer = customer };
        order.Raise(new @event.KeywordEvent(7));
        return order;
    }

    // Raises the event whose handler parks until a test releases it — see OutboxShutdownGraceTests.
    public static Order PlaceRaisingGated()
    {
        var order = new Order { Id = Guid.NewGuid(), Customer = "gated" };
        order.Raise(new GatedEvent());
        return order;
    }

    // Raises the event whose handler deletes a message out of the batch being drained — see SaboteurEvent.
    public static Order PlaceRaisingSaboteur()
    {
        var order = new Order { Id = Guid.NewGuid(), Customer = "saboteur" };
        order.Raise(new SaboteurEvent());
        return order;
    }
}

public sealed record OrderPlaced(Guid Id, string Customer) : IOutboxEvent;

/// <summary>
/// An event whose handler deletes the highest-numbered still-unprocessed outbox row — i.e. one sitting in the
/// very batch being drained. Stands in for anything writing to the outbox table underneath the processor.
/// </summary>
public sealed record SaboteurEvent : IOutboxEvent;

public sealed class SaboteurEventHandler(IDbContextFactory<OutboxDbContext> factory) : INotificationHandler<SaboteurEvent>
{
    public async Task HandleAsync(SaboteurEvent notification, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var doomed = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null)
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (doomed != 0)
        {
            await db.Set<OutboxMessage>()
                .Where(m => m.Id == doomed)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

public sealed class Recorder
{
    private readonly List<OrderPlaced> _events = [];
    public IReadOnlyList<OrderPlaced> Events
    {
        get { lock (_events) { return _events.ToArray(); } }
    }

    public void Add(OrderPlaced e)
    {
        lock (_events) { _events.Add(e); }
    }
}

public sealed class OrderPlacedHandler(Recorder recorder) : INotificationHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return Task.CompletedTask;
    }
}

public sealed class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(x => x.Id);
        modelBuilder.Entity<Order>().Property(x => x.Customer).IsRequired();
        modelBuilder.ApplyRaskConventions();
        modelBuilder.AddRaskOutbox();
    }
}

[Collection(OutboxDbCollection.Name)]
public sealed class OutboxTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-test-{Guid.NewGuid():N}.db");
    private readonly Recorder _recorder = new();
    private readonly @event.KeywordRecorder _keywordRecorder = new();
    private readonly ServiceProvider _provider;

    public OutboxTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_recorder);
        services.AddSingleton(_keywordRecorder);
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false); // the outbox owns delivery
        services.AddRaskOutbox<OutboxDbContext>(o => o.PollInterval = TimeSpan.FromMilliseconds(50));
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private OutboxDbContext NewContext() => _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    [Fact]
    public async Task Save_writes_an_outbox_message_in_the_same_transaction()
    {
        await using (var db = NewContext())
        {
            db.Orders.Add(Order.Place("ada"));
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var message = await read.Set<OutboxMessage>().SingleAsync();
        Assert.Contains("OrderPlaced", message.Type, StringComparison.Ordinal);
        Assert.Contains("ada", message.Payload, StringComparison.Ordinal);
        Assert.Null(message.ProcessedAt); // not yet drained
    }

    [Fact]
    public async Task The_processor_drains_the_outbox_and_publishes_events()
    {
        Guid id;
        await using (var db = NewContext())
        {
            var order = Order.Place("grace");
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            id = order.Id;
        }

        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            // Wait for the LAST thing the drain does to a message, not the first. It publishes (which is what
            // fills the recorder) and only then persists ProcessedAt — so waiting on the recorder and then
            // asserting on ProcessedAt races that save, and loses whenever the write is slow. Waiting on
            // ProcessedAt implies the publish already happened, so it covers both assertions below.
            await WaitUntilAsync(
                async () =>
                {
                    await using var poll = NewContext();
                    return await poll.Set<OutboxMessage>().AnyAsync(m => m.ProcessedAt != null);
                },
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains(_recorder.Events, e => e.Id == id && e.Customer == "grace");

        await using var read = NewContext();
        Assert.NotNull((await read.Set<OutboxMessage>().SingleAsync()).ProcessedAt); // marked processed
    }

    [Fact]
    public async Task An_event_in_a_keyword_namespace_is_delivered_not_dead_lettered()
    {
        await using (var db = NewContext())
        {
            db.Orders.Add(Order.PlaceRaisingKeywordEvent("ada"));
            await db.SaveChangesAsync();
        }

        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await using var poll = NewContext();
                    return await poll.Set<OutboxMessage>().AnyAsync(m => m.ProcessedAt != null);
                },
                TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        await using var read = NewContext();
        var message = await read.Set<OutboxMessage>().SingleAsync();

        // Delivered — and, the load-bearing half, delivered without a failed attempt. A key miss doesn't
        // throw: it records "No registered outbox event type '...'" and retries until MaxAttempts, so
        // asserting only on ProcessedAt would miss the bug entirely.
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.Error);
        Assert.Equal(1, message.Attempts); // attempts *started* — one claim, no failure (see OutboxMessage.Attempts)
        Assert.DoesNotContain('@', message.Type); // stored as the runtime name, unescaped
        Assert.Contains(_keywordRecorder.Events, e => e.N == 7); // the handler really ran
    }

    // The condition is async so it can poll the database — the only place the drain's completion is
    // observable — rather than an in-process side effect that runs earlier.
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(25);
        }
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

// A nested outbox event: its Type.FullName uses '+', which must still match the generator's dotted registration.
public sealed class OuterScope
{
    public sealed record NestedEvent(int N) : IOutboxEvent;
}

public sealed class OutboxSerializerRegistryTests
{
    [Fact]
    public void A_nested_event_type_round_trips()
    {
        // The Rask.Outbox source generator registered this assembly's IOutboxEvent types at module load.
        var (type, payload) = OutboxSerializerRegistry.Serialize(new OuterScope.NestedEvent(7));

        Assert.DoesNotContain('+', type); // stored dotted, matching the generator's registration
        var back = OutboxSerializerRegistry.Deserialize(type, payload);
        Assert.Equal(7, Assert.IsType<OuterScope.NestedEvent>(back).N);
    }
}
