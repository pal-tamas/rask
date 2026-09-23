using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class NotifyTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Send_reaches_a_subscriber_with_nothing_injected()
    {
        await using var services = Build();
        using var bound = Notify.UseScope(services);
        var dispatcher = services.GetRequiredService<IDispatcher>();

        var stop = new CancellationTokenSource();
        var reports = dispatcher.SubscribeAsync<Reported>(stop.Token).GetAsyncEnumerator(stop.Token);
        var next = reports.MoveNextAsync().AsTask();

        await Notify.Send(new Reported(7));

        Assert.True(await next.WaitAsync(Wait));
        Assert.Equal(new Reported(7), reports.Current);

        await stop.CancelAsync();
    }

    [Fact]
    public async Task Send_runs_the_handlers()
    {
        var recorder = new Reports();
        await using var services = Build(recorder);
        using var bound = Notify.UseScope(services);

        await Notify.Send(new Reported(3));

        Assert.Equal([3], recorder.Seen);
    }

    [Fact]
    public async Task Send_opens_a_scope_of_its_own_when_none_is_bound()
    {
        var recorder = new Reports();
        await using var services = Build(recorder);
        Notify.Configure(services);

        // No UseScope: this is the background-job path, where there is no scope to publish on and the facade has
        // to open one before a handler that needs scoped services can run.
        await Notify.Send(new Reported(11));

        Assert.Equal([11], recorder.Seen);
    }

    [Fact]
    public async Task Send_rejects_null()
    {
        await using var services = Build();
        using var bound = Notify.UseScope(services);

        await Assert.ThrowsAsync<ArgumentNullException>(() => Notify.Send<Reported>(null!));
    }

    [Fact]
    public async Task Building_a_container_configures_the_facade()
    {
        await using var services = Build();

        // Resolving a dispatcher is what hands Notify the container's root.
        _ = services.GetRequiredService<IDispatcher>();

        Assert.True(Notify.IsConfigured);
    }

    [Fact]
    public async Task A_bound_scope_is_restored_when_its_handle_is_disposed()
    {
        await using var outer = Build();
        await using var inner = Build();

        using (Notify.UseScope(outer))
        {
            using (Notify.UseScope(inner))
            {
                Assert.True(Notify.IsConfigured);
            }

            // The inner binding is gone; the outer one must still stand, or a nested publish would escape to
            // whatever container happened to be configured last.
            await Notify.Send(new Reported(1));
        }
    }

    private static ServiceProvider Build(Reports? reports = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(reports ?? new Reports());
        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }
}

public sealed record Reported(int Number) : INotification;

public sealed class Reports
{
    public List<int> Seen { get; } = [];
}

public sealed class RecordReport(Reports reports) : INotificationHandler<Reported>
{
    public Task HandleAsync(Reported notification, CancellationToken cancellationToken)
    {
        reports.Seen.Add(notification.Number);
        return Task.CompletedTask;
    }
}
