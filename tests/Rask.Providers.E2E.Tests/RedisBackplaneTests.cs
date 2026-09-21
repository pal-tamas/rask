using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.Redis;
using StackExchange.Redis;

#pragma warning disable RASK014 // a test-defined Component subclass has no generated factory

namespace Rask.Providers.E2E.Tests;

// #1115: two hosts, one Redis. Each host is its own container with its own hub, backplane and connection, exactly as
// two app instances behind a load balancer are; only the server between them is shared.
public sealed class RedisBackplaneTests
{
    private static readonly Topic<Order> Orders = new("orders", BackplaneJson.Default.Order);
    private static readonly Topic<Order> LocalOrders = new("local-orders");

    [SkippableFact]
    public async Task A_publish_on_one_host_reaches_the_other_host_s_subscribers_and_its_own_once()
    {
        Skip.IfNot(Redis.Available, Redis.SkipReason);
        var prefix = $"rask-test-{Guid.NewGuid():N}:";
        await using var publisher = await HostAsync(prefix);
        await using var other = await HostAsync(prefix);

        var onPublisher = 0;
        var onOther = new TaskCompletionSource<Order>(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.Hub.Subscribe(new Probe(), Orders, _ => Interlocked.Increment(ref onPublisher));
        other.Hub.Subscribe(new Probe(), Orders, order => onOther.TrySetResult(order));
        await ListenersAsync(prefix + "orders", 2);

        await publisher.Hub.PublishAsync(Orders, new Order(7, "Ada"));

        Assert.Equal(new Order(7, "Ada"), await onOther.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        // Redis hands the publisher its own message back too; the host id in the envelope is what drops it. Give the
        // echo the time it would take to arrive, then count.
        await Task.Delay(500);
        Assert.Equal(1, Volatile.Read(ref onPublisher));
    }

    [SkippableFact]
    public async Task A_topic_declared_without_a_contract_stays_on_its_host()
    {
        Skip.IfNot(Redis.Available, Redis.SkipReason);
        var prefix = $"rask-test-{Guid.NewGuid():N}:";
        await using var publisher = await HostAsync(prefix);
        await using var other = await HostAsync(prefix);

        var onOther = 0;
        other.Hub.Subscribe(new Probe(), LocalOrders, _ => Interlocked.Increment(ref onOther));

        await publisher.Hub.PublishAsync(LocalOrders, new Order(1, "Ada"));
        await Task.Delay(500);

        Assert.Equal(0, Volatile.Read(ref onOther));
        Assert.Equal(0, await ListenerCountAsync(prefix + "local-orders"));
    }

    private static async Task<AppHost> HostAsync(string prefix)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:Redis", Redis.Required)])
            .Build());

        // What Rask.Server registers, and what AddRaskRedisBackplane hands it.
        services.AddSingleton<IBroadcast, BroadcastHub>();
        services.AddRaskRedisBackplane(o => o.ChannelPrefix = prefix);

        var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        return new AppHost(provider);
    }

    // A host subscribes to Redis in the background after its first local subscriber, so wait until the server sees it.
    private static async Task ListenersAsync(string channel, long expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (await ListenerCountAsync(channel) < expected)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Redis never saw {expected} subscribers on {channel}.");
            await Task.Delay(50);
        }
    }

    private static async Task<long> ListenerCountAsync(string channel)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(Redis.Required);
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        return await server.SubscriptionSubscriberCountAsync(RedisChannel.Literal(channel));
    }

    public sealed record Order(int Id, string Customer);

    private sealed class AppHost(ServiceProvider provider) : IAsyncDisposable
    {
        public IBroadcast Hub { get; } = provider.GetRequiredService<IBroadcast>();

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    // Mounted in no session, so the hub runs its handler as the message arrives.
    private sealed class Probe : Component
    {
        protected override Component? Render() => null;
    }
}

[JsonSerializable(typeof(RedisBackplaneTests.Order))]
internal sealed partial class BackplaneJson : JsonSerializerContext;
