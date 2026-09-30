using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

public sealed record Shipped(Guid Id) : IEvent;

/// <summary>What ran, in order, and a switch that makes the charge fail.</summary>
public sealed class ShippingLog
{
    private readonly List<string> _ran = [];

    public bool ChargeFails { get; set; }

    public IReadOnlyList<string> Ran
    {
        get { lock (_ran) { return [.. _ran]; } }
    }

    public void Add(string what)
    {
        lock (_ran) { _ran.Add(what); }
    }
}

public sealed class RefreshDashboard(ShippingLog log) : IEventHandler<Shipped>
{
    public Task Handle(Shipped e)
    {
        log.Add("dashboard");
        return Task.CompletedTask;
    }
}

public sealed class SendShippingReceipt(ShippingLog log) : IDurableHandler<Shipped>
{
    public Task Handle(Shipped e)
    {
        log.Add("receipt");
        return Task.CompletedTask;
    }
}

public sealed class ChargeCard(ShippingLog log) : IDurableHandler<Shipped>
{
    public Task Handle(Shipped e)
    {
        if (log.ChargeFails)
        {
            throw new InvalidOperationException("card declined");
        }

        log.Add("charge");
        return Task.CompletedTask;
    }
}

/// <summary>A context that never mapped the outbox table.</summary>
public sealed class NoOutboxTableContext(DbContextOptions<NoOutboxTableContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Order>().HasKey(x => x.Id);
}

[Collection(OutboxDbCollection.Name)]
public sealed class DurableHandlerTests : IDisposable
{
    private readonly ShippingLog _log = new();
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
    public async Task A_save_runs_the_in_memory_handler_at_once_and_stores_one_row_per_durable_handler()
    {
        var provider = Build(Outbox);

        await Save(provider, new Shipped(Guid.NewGuid()));

        Assert.Equal(["dashboard"], _log.Ran);
        Assert.Equal(
            [Name<ChargeCard>(), Name<SendShippingReceipt>()],
            (await Rows(provider)).Select(m => m.Handler!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Each_stored_row_runs_its_own_durable_handler_once()
    {
        var provider = Build(Outbox);
        await Save(provider, new Shipped(Guid.NewGuid()));

        await Processor(provider).RunCycleAsync(CancellationToken.None);

        Assert.Equal(["charge", "dashboard", "receipt"], _log.Ran.Order(StringComparer.Ordinal));
        Assert.All(await Rows(provider), m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task A_failing_durable_handler_is_retried_alone()
    {
        var provider = Build(Outbox);
        await Save(provider, new Shipped(Guid.NewGuid()));
        _log.ChargeFails = true;
        await Processor(provider).RunCycleAsync(CancellationToken.None);
        _log.ChargeFails = false;

        await Processor(provider).RunCycleAsync(CancellationToken.None);

        Assert.Single(_log.Ran, "receipt");
        Assert.Single(_log.Ran, "charge");
        Assert.All(await Rows(provider), m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task A_bare_publish_stores_the_durable_rows_and_runs_the_in_memory_handler_now()
    {
        var provider = Build(Outbox);

        await provider.GetRequiredService<IDispatcher>().Publish(new Shipped(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(["dashboard"], _log.Ran);
        Assert.Equal(2, (await Rows(provider)).Count);
    }

    [Fact]
    public async Task The_outbox_may_be_registered_before_the_data_interceptors()
    {
        var provider = Build(static services =>
        {
            services.AddRaskOutbox<OutboxDbContext>();
            services.AddRaskData();
        });

        await Save(provider, new Shipped(Guid.NewGuid()));

        Assert.Equal(["dashboard"], _log.Ran);
        Assert.Equal(2, (await Rows(provider)).Count);
    }

    [Fact]
    public async Task Without_an_outbox_a_durable_handler_runs_in_memory_like_any_other()
    {
        var provider = Build(static services => services.AddRaskData());

        await Save(provider, new Shipped(Guid.NewGuid()));

        Assert.Equal(["charge", "dashboard", "receipt"], _log.Ran.Order(StringComparer.Ordinal));
        Assert.Empty(await Rows(provider));
    }

    [Fact]
    public async Task A_save_wakes_the_processor_without_waiting_for_the_poll()
    {
        var provider = Build(static services =>
            services.AddRaskOutbox<OutboxDbContext>(o =>
            {
                o.PollInterval = TimeSpan.FromHours(1);
                o.LeaseDuration = TimeSpan.FromHours(2);
            }));
        var processor = Processor(provider);
        await processor.StartAsync(CancellationToken.None);

        try
        {
            await Save(provider, new Shipped(Guid.NewGuid()));
            await WaitUntil(() => _log.Ran.Contains("receipt") && _log.Ran.Contains("charge"), TimeSpan.FromSeconds(10));
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }

        Assert.Contains("receipt", _log.Ran);
    }

    [Fact]
    public async Task With_a_durable_handler_an_unmapped_outbox_table_fails_the_boot_naming_the_line_to_add()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskCqrs();
        services.AddRaskOutbox<NoOutboxTableContext>();
        services.AddDbContextFactory<NoOutboxTableContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();
        var check = provider.GetServices<IHostedService>().Single(s => s.GetType().Name.StartsWith("OutboxModelCheck", StringComparison.Ordinal));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));

        Assert.Contains("modelBuilder.AddRaskOutbox()", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Turn the battery off", error.Message, StringComparison.Ordinal);
    }

    private static void Outbox(IServiceCollection services) => services.AddRaskOutbox<OutboxDbContext>();

    private static string Name<THandler>() =>
        $"{CqrsRegistry.NameOf(typeof(THandler))}:{CqrsRegistry.NameOf(typeof(Shipped))}";

    private ServiceProvider Build(Action<IServiceCollection> register)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-outbox-durable-{Guid.NewGuid():N}.db");
        _paths.Add(path);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        services.AddRaskCqrs();
        register(services);
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

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(25);
        }
    }
}
