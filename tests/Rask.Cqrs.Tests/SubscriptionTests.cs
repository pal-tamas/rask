using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class SubscriptionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_published_notification_reaches_every_subscriber()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using var first = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());
        await using var second = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());

        await dispatcher.PublishAsync(new Chimed(1));

        Assert.Equal(new Chimed(1), await first.NextAsync());
        Assert.Equal(new Chimed(1), await second.NextAsync());
    }

    [Fact]
    public async Task A_subscriber_hears_only_the_type_it_asked_for()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using var chimes = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());

        await dispatcher.PublishAsync(new Whistled(1));
        await dispatcher.PublishAsync(new Chimed(2));

        Assert.Equal(new Chimed(2), await chimes.NextAsync());
    }

    [Fact]
    public async Task A_subscription_starts_with_the_last_notification_published()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using (await ListenAsync(dispatcher.SubscribeAsync<Chimed>()))
        {
            await dispatcher.PublishAsync(new Chimed(1));
            await dispatcher.PublishAsync(new Chimed(2));
        }

        await using var late = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());

        Assert.Equal(new Chimed(2), await late.NextAsync());
    }

    [Fact]
    public async Task A_type_nobody_has_subscribed_to_is_not_remembered()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await dispatcher.PublishAsync(new Chimed(1));

        await using var late = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());

        Assert.False(late.HasNext);
    }

    [Fact]
    public async Task Publishing_runs_the_handlers_and_reaches_the_subscribers()
    {
        var recorder = new Recorder();
        await using var services = Build(recorder);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using var pings = await ListenAsync(dispatcher.SubscribeAsync<Pinged>());

        await dispatcher.PublishAsync(new Pinged("go"));

        Assert.Equal(new Pinged("go"), await pings.NextAsync());
        Assert.Equal(["A:go", "B:go"], recorder.Entries);
    }

    [Fact]
    public async Task A_scoped_notification_reaches_only_the_subscribers_watching_its_key()
    {
        var mine = Guid.NewGuid();
        var yours = Guid.NewGuid();
        await using var services = Build(allowed: [mine, yours]);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using var watching = await ListenAsync(dispatcher.SubscribeAsync<DoorOpened>(mine));

        await dispatcher.PublishAsync(new DoorOpened(yours));
        await dispatcher.PublishAsync(new DoorOpened(mine));

        Assert.Equal(new DoorOpened(mine), await watching.NextAsync());
    }

    [Fact]
    public async Task A_scoped_subscription_starts_with_the_last_notification_for_its_own_key()
    {
        var mine = Guid.NewGuid();
        var yours = Guid.NewGuid();
        await using var services = Build(allowed: [mine, yours]);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using (await ListenAsync(dispatcher.SubscribeAsync<DoorOpened>(mine)))
        {
            await dispatcher.PublishAsync(new DoorOpened(mine));
            await dispatcher.PublishAsync(new DoorOpened(yours));
        }

        await using var late = await ListenAsync(dispatcher.SubscribeAsync<DoorOpened>(mine));

        Assert.Equal(new DoorOpened(mine), await late.NextAsync());
    }

    [Fact]
    public async Task The_policy_refuses_a_key_it_does_not_allow()
    {
        await using var services = Build(allowed: [Guid.NewGuid()]);
        var dispatcher = services.GetRequiredService<IDispatcher>();

        var refused = dispatcher.SubscribeAsync<DoorOpened>(Guid.NewGuid()).GetAsyncEnumerator();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task A_scope_with_no_policy_lets_nobody_watch()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();

        var refused = dispatcher.SubscribeAsync<VaultOpened>(7).GetAsyncEnumerator();

        var error = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refused.MoveNextAsync().AsTask());
        Assert.Contains("IWatchPolicy<Vault>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_scoped_notification_needs_a_key_of_its_own_type_and_an_unscoped_one_takes_none()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();

        var missing = dispatcher.SubscribeAsync<DoorOpened>().GetAsyncEnumerator();
        var wrongType = dispatcher.SubscribeAsync<DoorOpened>("not a guid").GetAsyncEnumerator();
        var unexpected = dispatcher.SubscribeAsync<Chimed>(1).GetAsyncEnumerator();

        await Assert.ThrowsAsync<ArgumentException>(() => missing.MoveNextAsync().AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => wrongType.MoveNextAsync().AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => unexpected.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task Ending_a_subscription_stops_it_listening()
    {
        await using var services = Build();
        var dispatcher = services.GetRequiredService<IDispatcher>();
        var feed = services.GetRequiredService<NotificationFeed>();
        var listening = await ListenAsync(dispatcher.SubscribeAsync<Chimed>());

        await listening.DisposeAsync();

        Assert.Equal(0, feed.ListenerCount(typeof(Chimed)));
    }

    [Fact]
    public async Task The_replay_store_forgets_the_oldest_key_past_its_capacity()
    {
        var keys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        await using var services = Build(allowed: [keys[0]], capacity: 4);
        var dispatcher = services.GetRequiredService<IDispatcher>();
        await using (await ListenAsync(dispatcher.SubscribeAsync<DoorOpened>(keys[0])))
        {
            foreach (var key in keys)
            {
                await dispatcher.PublishAsync(new DoorOpened(key));
            }
        }

        await using var late = await ListenAsync(dispatcher.SubscribeAsync<DoorOpened>(keys[0]));

        Assert.False(late.HasNext);
    }

    [Fact]
    public async Task The_generator_registers_the_watch_policy()
    {
        await using var services = Build();

        using var scope = services.CreateScope();

        Assert.IsType<DoorPolicy>(scope.ServiceProvider.GetService<IWatchPolicy<Door>>());
    }

    [Fact]
    public void The_generator_records_how_each_key_is_read_and_crosses_the_wire()
    {
        var door = Guid.NewGuid();

        var opened = CqrsRegistry.FindNotificationScope(typeof(DoorOpened))!;
        var tagged = CqrsRegistry.FindNotificationScope(typeof(DoorTagged))!;
        var painted = CqrsRegistry.FindNotificationScope(typeof(DoorPainted))!;
        var knocked = CqrsRegistry.FindNotificationScope(typeof(DoorKnocked))!;

        Assert.Equal(typeof(Door), opened.Scope);
        Assert.Equal(door, opened.KeyOf(new DoorOpened(door)));
        Assert.Equal(door, opened.ParseKey!(door.ToString()));
        Assert.Equal("front", tagged.ParseKey!("front"));
        Assert.Equal(Colour.Green, painted.ParseKey!("Green"));
        Assert.Equal(12L, knocked.KeyOf(new DoorKnocked { DoorNumber = 12 }));
        Assert.Equal(12L, knocked.ParseKey!("12"));
        Assert.Null(CqrsRegistry.FindNotificationScope(typeof(Chimed)));
    }

    [Fact]
    public void The_subscription_knobs_are_read_from_configuration_and_code_wins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Rask:Cqrs:ReplayCapacity", "16"),
                new KeyValuePair<string, string?>("Rask:Cqrs:SubscriptionBuffer", "32"),
                new KeyValuePair<string, string?>("Rask:Cqrs:SubscriptionReconnectDelay", "00:00:00.250"),
                new KeyValuePair<string, string?>("Rask:Cqrs:SubscriptionReconnectCeiling", "00:00:05"),
            ]).Build();

        var options = new CqrsOptions();
        CqrsServiceCollectionExtensions.ApplyConfiguration(options, configuration);
        var overridden = new CqrsOptions();
        CqrsServiceCollectionExtensions.ApplyConfiguration(overridden, configuration);
        overridden.ReplayCapacity = 99;

        Assert.Equal(16, options.ReplayCapacity);
        Assert.Equal(32, options.SubscriptionBuffer);
        Assert.Equal(TimeSpan.FromMilliseconds(250), options.SubscriptionReconnectDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), options.SubscriptionReconnectCeiling);
        Assert.Equal(99, overridden.ReplayCapacity);
    }

    [Fact]
    public void A_ceiling_shorter_than_the_first_delay_is_refused()
    {
        var options = new CqrsOptions { SubscriptionReconnectCeiling = TimeSpan.FromMilliseconds(100) };

        var error = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("SubscriptionReconnectCeiling", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(Recorder? recorder = null, object[]? allowed = null, int? capacity = null)
    {
        var keys = new Keyholder();
        foreach (var key in allowed ?? [])
        {
            keys.Allowed.Add(key);
        }

        var services = new ServiceCollection();
        services.AddSingleton(recorder ?? new Recorder());
        services.AddSingleton(keys);
        services.AddRaskCqrs(o =>
        {
            if (capacity is { } replay)
            {
                o.ReplayCapacity = replay;
            }
        });
        return services.BuildServiceProvider();
    }

    // Opens the subscription and waits until it is listening: the first MoveNext runs the policy and registers with the
    // feed synchronously for every policy here, so once it has been called a publish reaches it.
    private static Task<Listening<T>> ListenAsync<T>(IAsyncEnumerable<T> source)
    {
        var stop = new CancellationTokenSource();
        var enumerator = source.GetAsyncEnumerator(stop.Token);
        return Task.FromResult(new Listening<T>(enumerator, stop));
    }

    private sealed class Listening<T>(IAsyncEnumerator<T> enumerator, CancellationTokenSource stop) : IAsyncDisposable
    {
        private Task<bool> _next = enumerator.MoveNextAsync().AsTask();

        /// <summary>Whether a value was already waiting — a replay — the moment the subscription opened.</summary>
        public bool HasNext => _next.IsCompleted;

        public async Task<T> NextAsync()
        {
            Assert.True(await _next.WaitAsync(Wait));
            var value = enumerator.Current;
            _next = enumerator.MoveNextAsync().AsTask();
            return value;
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            try
            {
                await _next;
            }
            catch (OperationCanceledException)
            {
            }

            await enumerator.DisposeAsync();
            stop.Dispose();
        }
    }
}
