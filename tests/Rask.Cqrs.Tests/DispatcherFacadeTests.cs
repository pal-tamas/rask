using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

[Collection(DispatcherFacadeCollection.Name)]
public sealed class DispatcherFacadeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Publish_reaches_a_subscriber_with_nothing_injected()
    {
        await using var services = Build();
        using var work = Ambient.Enter(services);
        var stop = new CancellationTokenSource();
        var reports = services.GetRequiredService<IDispatcher>().Subscribe<Reported>(stop.Token).GetAsyncEnumerator(stop.Token);
        var next = reports.MoveNextAsync().AsTask();

        await Dispatcher.Publish(new Reported(7), TestContext.Current.CancellationToken);

        Assert.True(await next.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.Equal(new Reported(7), reports.Current);
        await stop.CancelAsync();
    }

    [Fact]
    public async Task Publish_runs_the_handlers_of_the_work_in_progress()
    {
        var recorder = new Reports();
        await using var services = Build(recorder);
        using var work = Ambient.Enter(services);

        await Dispatcher.Publish(new Reported(3), TestContext.Current.CancellationToken);

        Assert.Equal([3], recorder.Seen);
    }

    [Fact]
    public async Task Publish_opens_a_scope_of_its_own_outside_any_work()
    {
        var recorder = new Reports();
        await using var services = Build(recorder);
        // Resolving a dispatcher is what hands the facade the container's root. Nothing is ambient: this is the
        // hosted-service path, where the facade has to open a scope before a handler can run.
        _ = services.GetRequiredService<IDispatcher>();

        await Dispatcher.Publish(new Reported(11), TestContext.Current.CancellationToken);

        Assert.Equal([11], recorder.Seen);
    }

    [Fact]
    public async Task A_handler_reached_outside_any_work_finds_the_app_through_the_facades()
    {
        var recorder = new Reports();
        await using var services = Build(recorder);
        _ = services.GetRequiredService<IDispatcher>();

        await Dispatcher.Publish(new Relayed(5), TestContext.Current.CancellationToken);

        Assert.Equal([5], recorder.Seen);
    }

    [Fact]
    public async Task Building_a_container_turns_the_facade_on()
    {
        await using var services = Build();

        _ = services.GetRequiredService<IDispatcher>();

        Assert.True(Dispatcher.IsOn);
    }

    private static ServiceProvider Build(Reports? reports = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reports ?? new Reports());
        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }
}

public sealed record Reported(int Number) : IEvent;

/// <summary>An event whose handler publishes another through the facade.</summary>
public sealed record Relayed(int Number) : IEvent;

public sealed class Reports
{
    public List<int> Seen { get; } = [];
}

public sealed class RecordReport(Reports reports) : IEventHandler<Reported>
{
    public Task Handle(Reported e)
    {
        reports.Seen.Add(e.Number);
        return Task.CompletedTask;
    }
}

public sealed class RelayReport : IEventHandler<Relayed>
{
    public Task Handle(Relayed e) => Dispatcher.Publish(new Reported(e.Number));
}
