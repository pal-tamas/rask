using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Messaging;
using Rask.Hosting.Shared;

namespace Rask.Redis;

/// <summary>Registers the Redis broadcast backplane into an <see cref="IServiceCollection" />.</summary>
public static class RaskRedisServiceCollectionExtensions
{
    /// <summary>
    /// Carries <c>IBroadcast</c> messages between the app's hosts over Redis pub/sub, so a publish on one server reaches
    /// the subscribed pages on every server. Only topics declared with a <c>JsonTypeInfo</c> —
    /// <c>new Topic&lt;OrderPlaced&gt;("orders", AppJson.Default.OrderPlaced)</c> — cross; every other topic stays in
    /// its process. The connection string is <c>Rask:ConnectionStrings:Redis</c>, or the app's own registered
    /// <c>IConnectionMultiplexer</c>; <see cref="RedisOptions" /> reads the <c>Rask:Redis</c> section first and then
    /// <paramref name="configure" />, so code wins. Idempotent.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="configure">Changes <see cref="RedisOptions" /> after configuration is read.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskRedisBackplane(
        this IServiceCollection services, Action<RedisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.AddRaskOptions<RedisOptions>("Rask:Redis", static (section, o) => section.Bind(o), configure,
                static o => o.Validate()))
        {
            return services;
        }

        services.TryAddSingleton<RedisBroadcastBackplane>();
        services.TryAddSingleton<IBroadcastBackplane>(static sp => sp.GetRequiredService<RedisBroadcastBackplane>());
        services.AddSingleton<IHostedService>(static sp => sp.GetRequiredService<RedisBroadcastBackplane>());
        return services;
    }
}
