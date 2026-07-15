using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

// An aggregate that raises an outbox event on creation.
public sealed class Order : AggregateRoot<Guid>
{
    private Order() { }

    public string Customer { get; private set; } = "";

    public static Order Place(string customer)
    {
        var order = new Order { Id = Guid.NewGuid(), Customer = customer };
        order.Raise(new OrderPlaced(order.Id, customer));
        return order;
    }
}

public sealed record OrderPlaced(Guid Id, string Customer) : IOutboxEvent;

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

public sealed class OutboxTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-test-{Guid.NewGuid():N}.db");
    private readonly Recorder _recorder = new();
    private readonly ServiceProvider _provider;

    public OutboxTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_recorder);
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
            // Wait for the LAST thing the drain does, not the first. It publishes the whole batch (which is
            // what fills the recorder) and only then persists ProcessedAt, in a single end-of-batch
            // SaveChangesAsync — so waiting on the recorder and then asserting on ProcessedAt races that
            // save, and loses whenever the write is slow. Waiting on ProcessedAt implies the publish already
            // happened, so it covers both assertions below.
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
