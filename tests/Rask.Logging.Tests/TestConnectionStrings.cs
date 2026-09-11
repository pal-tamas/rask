using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.Logging;

/// <summary>
/// <c>AddRaskLogging</c> reads its connection string from <c>Rask:ConnectionStrings:Logs</c>. These tests give every
/// container its own throwaway store, so this registers exactly that one key as the container's configuration and
/// then calls the real overload.
/// </summary>
internal static class TestConnectionStrings
{
    internal static IServiceCollection AddRaskLoggingAt(
        this IServiceCollection services, string connectionString, Action<RaskLoggingOptions>? configure = null)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:Logs", connectionString)])
            .Build());
        return services.AddRaskLogging(configure);
    }
}
