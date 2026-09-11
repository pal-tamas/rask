using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.Postgres;
using Rask.SqlServer;

/// <summary>
/// <c>UseRaskPostgres</c> and <c>UseRaskSqlServer</c> read their connection string from <c>Rask:ConnectionStrings:App</c>.
/// These tests point every context at the server the gate started, so this hands the real overloads a service provider
/// carrying exactly that one key.
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

    internal static DbContextOptionsBuilder UseRaskSqlServerAt(
        this DbContextOptionsBuilder builder, string connectionString, Action<SqlServerOptions>? configure = null) =>
        builder.UseRaskSqlServer(App(connectionString), configure);

    internal static DbContextOptionsBuilder<TContext> UseRaskSqlServerAt<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString, Action<SqlServerOptions>? configure = null)
        where TContext : DbContext =>
        builder.UseRaskSqlServer(App(connectionString), configure);

    private static IServiceProvider App(string connectionString) =>
        new ConfigurationServices(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)])
            .Build());

    // Just the configuration: that is all the provider overloads ask the service provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
