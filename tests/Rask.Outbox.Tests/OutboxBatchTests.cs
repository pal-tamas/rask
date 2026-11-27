using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

/// <summary>A scoped service: which scope a durable handler ran in.</summary>
public sealed class ScopeProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed record ScopedEvent(int N) : IEvent;

public sealed class ScopedEventHandler(ScopeProbe probe, ScopeLog log) : IDurableHandler<ScopedEvent>
{
    public Task Handle(ScopedEvent notification)
    {
        log.Add(probe.Id);
        return Task.CompletedTask;
    }
}

public sealed class ScopeLog
{
    private readonly List<Guid> _scopes = [];

    public IReadOnlyList<Guid> Scopes
    {
        get { lock (_scopes) { return _scopes.ToArray(); } }
    }

    public void Add(Guid scope)
    {
        lock (_scopes) { _scopes.Add(scope); }
    }
}

/// <summary>
///     What one message of a claimed batch can do to the others: nothing. A payload that no longer reads is that
///     message's failure, each handler has a scope of its own, and the batch keeps its claim for as long as it takes.
/// </summary>
public sealed class OutboxBatchTests : IDisposable
{
    private readonly InMemorySqlite _db = new();
    private readonly Recorder _recorder = new();
    private readonly ScopeLog _scopes = new();
    private readonly OutboxGate _gate = new();
    private readonly ManualClock _clock = new();
    private readonly ServiceProvider _provider;

    public OutboxBatchTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_recorder);
        services.AddSingleton(_scopes);
        services.AddSingleton(_gate);
        services.AddScoped<ScopeProbe>();
        services.AddSingleton<TimeProvider>(_clock); // first, so AddRaskOutbox's TryAddSingleton keeps it
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddRaskOutbox<OutboxDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(20);
            o.LeaseDuration = TimeSpan.FromMilliseconds(300);
        });
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite(_db.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private static TimeSpan HangCeiling => TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OutboxProcessor<OutboxDbContext> Processor =>
        _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();

    [Fact]
    public async Task A_payload_that_no_longer_reads_fails_its_message_and_the_batch_goes_on()
    {
        await SaveAsync(Order.Place("broken"));
        await SaveAsync(Order.Place("after"));
        await using (var db = NewContext())
        {
            await db.Set<OutboxMessage>()
                .Where(m => m.Payload.Contains("broken"))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Payload, "{not json"), Ct);
        }

        await Processor.RunCycleAsync(Ct);

        await using var read = NewContext();
        var broken = await read.Set<OutboxMessage>().SingleAsync(m => m.Payload == "{not json", Ct);
        Assert.Null(broken.ProcessedAt);
        Assert.Null(broken.ClaimToken);
        Assert.Contains("does not read", broken.Error, StringComparison.Ordinal);
        Assert.Equal(["after"], _recorder.Events.Select(e => e.Customer));
    }

    [Fact]
    public async Task Each_message_of_a_batch_is_handled_in_a_scope_of_its_own()
    {
        await SaveAsync(Order.PlaceRaising(new ScopedEvent(1)));
        await SaveAsync(Order.PlaceRaising(new ScopedEvent(2)));

        await Processor.RunCycleAsync(Ct);

        Assert.Equal(2, _scopes.Scopes.Distinct().Count());
    }

    [Fact]
    public async Task A_batch_that_outlasts_its_lease_keeps_it_while_it_publishes()
    {
        await SaveAsync(Order.PlaceRaisingGated());
        await SaveAsync(Order.Place("second"));
        var cycle = Processor.RunCycleAsync(Ct);
        await _gate.Entered.Task.WaitAsync(HangCeiling, Ct);

        // Far past the claim's lease while the first handler still runs: only a renewal keeps the second message ours.
        _clock.Advance(TimeSpan.FromHours(1));
        var renewed = await SecondIsRenewedAsync();
        _gate.Release.SetResult();
        await cycle;

        Assert.True(renewed);
        Assert.Equal(["second"], _recorder.Events.Select(e => e.Customer));
    }

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    private OutboxDbContext NewContext() => _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    // Whether the second message's claim reaches past the moved clock before the hang ceiling: renewed, not expired.
    private async Task<bool> SecondIsRenewedAsync()
    {
        var deadline = DateTime.UtcNow + HangCeiling;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = NewContext();
            var now = Timestamp.At(_clock.GetUtcNow());
            if (await db.Set<OutboxMessage>().AnyAsync(m => m.Payload.Contains("second") && m.ClaimedUntil > now, Ct))
            {
                return true;
            }

            await Task.Delay(20, Ct);
        }

        return false;
    }

    private async Task SaveAsync(Order order)
    {
        await using var db = NewContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
    }
}
