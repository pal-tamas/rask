using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

/// <summary>An event whose handler parks until the test releases it — the lever for the shutdown-grace tests.</summary>
public sealed record GatedEvent : IOutboxEvent;

/// <summary>Latches for driving a handler across a shutdown: entered → (test acts) → released.</summary>
public sealed class OutboxGate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class GatedEventHandler(OutboxGate gate) : INotificationHandler<GatedEvent>
{
    public async Task HandleAsync(GatedEvent notification, CancellationToken cancellationToken)
    {
        gate.Entered.TrySetResult();
        // Observes the token, so a grace expiry actually cancels it.
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        gate.Completed.TrySetResult();
    }
}

/// <summary>
///     What happens to an outbox message that is already being published when the host is asked to stop.
///     Before <c>ShutdownGracePeriod</c>, the host's stopping token went straight into the handler, so
///     <c>SIGTERM</c> cancelled a publish mid-call.
/// </summary>
public sealed class OutboxShutdownGraceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-grace-{Guid.NewGuid():N}.db");
    private ServiceProvider? _provider;

    public void Dispose()
    {
        _provider?.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task An_in_flight_publish_finishes_within_the_grace()
    {
        var gate = Build(TimeSpan.FromSeconds(5));
        await EnqueueGatedAsync();

        await Processor.StartAsync(CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stop = Processor.StopAsync(CancellationToken.None);
        gate.Release.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(gate.Completed.Task.IsCompletedSuccessfully, "the handler ran to completion");
        var message = await SingleMessageAsync();
        Assert.NotNull(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public async Task A_grace_expiry_does_not_count_a_failed_attempt()
    {
        // MaxAttempts is 10 here: counting a redeploy as an attempt would let ten unlucky deploys abandon
        // a message nobody ever failed to publish.
        var gate = Build(TimeSpan.FromMilliseconds(50));
        await EnqueueGatedAsync();

        await Processor.StartAsync(CancellationToken.None);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        var message = await SingleMessageAsync();
        Assert.Null(message.ProcessedAt);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.Error);
    }

    [Fact]
    public void AddRaskOutbox_now_validates_its_options()
    {
        // Outbox was the only battery whose registration never validated (Jobs, Mail and Cache all did), so
        // PollInterval = Zero used to throw out of `new PeriodicTimer(...)` on the background thread and
        // take the host down at an unrelated moment. Closes #562.
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddRaskOutbox<OutboxDbContext>(o => o.PollInterval = TimeSpan.Zero));
    }

    [Fact]
    public void A_negative_grace_is_rejected_at_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddRaskOutbox<OutboxDbContext>(o => o.ShutdownGracePeriod = TimeSpan.FromSeconds(-1)));
    }

    private IHostedService Processor =>
        _provider!.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();

    private OutboxGate Build(TimeSpan grace)
    {
        var gate = new OutboxGate();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(gate);
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
        services.AddRaskOutbox<OutboxDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(20);
            o.ShutdownGracePeriod = grace;
        });
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
        return gate;
    }

    private OutboxDbContext NewContext() =>
        _provider!.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    private async Task EnqueueGatedAsync()
    {
        // Raises only the gated event, so the batch holds exactly one message and the assertions below are
        // unambiguous.
        await using var db = NewContext();
        db.Orders.Add(Order.PlaceRaisingGated());
        await db.SaveChangesAsync();
    }

    private async Task<OutboxMessage> SingleMessageAsync()
    {
        await using var db = NewContext();
        return await db.Set<OutboxMessage>().SingleAsync();
    }
}
