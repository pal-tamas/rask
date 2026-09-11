using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rask.SQLite;

/// <summary>
/// <c>AddRaskSqlite</c> and <c>UseRaskSqlite</c> read their connection string from <c>Rask:ConnectionStrings:App</c>.
/// Each scenario measures its own database file, so these hand the real overloads exactly that one key.
/// </summary>
internal static class TestConnectionStrings
{
    internal static IServiceCollection AddRaskSqliteAt(
        this IServiceCollection services, string connectionString, Action<SqliteOptions>? configure = null)
    {
        services.AddSingleton(Configuration(connectionString));
        return services.AddRaskSqlite(configure);
    }

    internal static DbContextOptionsBuilder UseRaskSqliteAt(
        this DbContextOptionsBuilder builder, string connectionString, Action<SqliteOptions>? configure = null) =>
        builder.UseRaskSqlite(new ConfigurationServices(Configuration(connectionString)), configure);

    internal static DbContextOptionsBuilder<TContext> UseRaskSqliteAt<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString, Action<SqliteOptions>? configure = null)
        where TContext : DbContext =>
        builder.UseRaskSqlite(new ConfigurationServices(Configuration(connectionString)), configure);

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)])
            .Build();

    // Just the configuration: that is all UseRaskSqlite asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
