using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Core.Messaging;

namespace Rask.Redis.Tests;

public sealed class AddRaskRedisBackplaneTests
{
    [Fact]
    public void The_broadcast_hub_picks_the_backplane_up()
    {
        using var services = Build();

        var hub = Assert.IsType<BroadcastHub>(services.GetRequiredService<IBroadcast>());

        Assert.IsType<RedisBroadcastBackplane>(hub.Backplane);
    }

    [Fact]
    public void Registering_twice_registers_once()
    {
        var collection = Services();

        collection.AddRaskRedisBackplane();
        collection.AddRaskRedisBackplane(o => o.ChannelPrefix = "ignored:");
        using var services = collection.BuildServiceProvider();

        Assert.Single(services.GetServices<IHostedService>());
        Assert.Equal("rask:broadcast:", services.GetRequiredService<IOptions<RedisOptions>>().Value.ChannelPrefix);
    }

    [Fact]
    public void The_section_is_read_and_code_wins()
    {
        using var fromConfig = Build(("Rask:Redis:ChannelPrefix", "shop:"));
        using var fromCode = Build([("Rask:Redis:ChannelPrefix", "shop:")], o => o.ChannelPrefix = "code:");

        Assert.Equal("shop:", fromConfig.GetRequiredService<IOptions<RedisOptions>>().Value.ChannelPrefix);
        Assert.Equal("code:", fromCode.GetRequiredService<IOptions<RedisOptions>>().Value.ChannelPrefix);
    }

    [Fact]
    public void An_empty_channel_prefix_is_refused() =>
        Assert.Throws<OptionsValidationException>(() =>
        {
            using var services = Build([], o => o.ChannelPrefix = " ");
            _ = services.GetRequiredService<IOptions<RedisOptions>>().Value;
        });

    [Fact]
    public async Task A_missing_connection_string_stops_the_start_and_names_the_key()
    {
        using var services = Build();
        var start = services.GetServices<IHostedService>().Single();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => start.StartAsync(CancellationToken.None));

        Assert.Contains("Rask:ConnectionStrings:Redis", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings) => Build(settings, null);

    private static ServiceProvider Build((string Key, string Value)[] settings, Action<RedisOptions>? configure)
    {
        var collection = Services(settings);
        collection.AddRaskRedisBackplane(configure);
        return collection.BuildServiceProvider();
    }

    // What Rask.Server registers: the hub is TryAdd'ed, and takes the backplane when the container has one.
    private static ServiceCollection Services(params (string Key, string Value)[] settings)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());
        collection.AddSingleton<IBroadcast, BroadcastHub>();
        return collection;
    }
}
