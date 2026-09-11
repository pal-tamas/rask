using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.Postgres;

/// <summary>
/// <c>UseRaskPostgres</c> reads its connection string from <c>Rask:ConnectionStrings:App</c>. These tests point every
/// context at the server the gate started, so this hands the real overload a service provider carrying exactly that
/// one key.
/// </summary>
internal static class TestConnectionStrings
{
    internal static DbContextOptionsBuilder UseRaskPostgresAt(
        this DbContextOptionsBuilder builder, string connectionString, Action<PostgresOptions>? configure = null) =>
        builder.UseRaskPostgres(App(connectionString), configure);

    internal static DbContextOptionsBuilder<TContext> UseRaskPostgresAt<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString, Action<PostgresOptions>? configure = null)
        where TContext : DbContext =>
        builder.UseRaskPostgres(App(connectionString), configure);

    private static IServiceProvider App(string connectionString) =>
        new ConfigurationServices(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)])
            .Build());

    // Just the configuration: that is all UseRaskPostgres asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
