using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.SQLite;

/// <summary>
/// <c>AddRaskSqlite</c> reads its connection string from <c>Rask:ConnectionStrings:App</c>. These tests give every
/// container its own throwaway database, so this registers exactly that one key as the container's configuration and
/// then calls the real overload.
/// </summary>
internal static class TestConnectionStrings
{
    internal static IServiceCollection AddRaskSqliteAt(
        this IServiceCollection services, string connectionString, Action<SqliteOptions>? configure = null)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)])
            .Build());
        return services.AddRaskSqlite(configure);
    }
}
