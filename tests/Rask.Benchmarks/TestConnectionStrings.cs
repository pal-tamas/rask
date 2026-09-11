using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.SQLite;

/// <summary>
/// <c>UseRaskSqlite</c> reads its connection string from <c>Rask:ConnectionStrings:App</c>. The bulk-insert benchmark
/// measures its own database file, so this hands the real overload exactly that one key.
/// </summary>
internal static class TestConnectionStrings
{
    internal static DbContextOptionsBuilder UseRaskSqliteAt(
        this DbContextOptionsBuilder builder, string connectionString, Action<SqliteOptions>? configure = null) =>
        builder.UseRaskSqlite(App(connectionString), configure);

    internal static DbContextOptionsBuilder<TContext> UseRaskSqliteAt<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString, Action<SqliteOptions>? configure = null)
        where TContext : DbContext =>
        builder.UseRaskSqlite(App(connectionString), configure);

    private static IServiceProvider App(string connectionString) =>
        new ConfigurationServices(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", connectionString)])
            .Build());

    // Just the configuration: that is all UseRaskSqlite asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
