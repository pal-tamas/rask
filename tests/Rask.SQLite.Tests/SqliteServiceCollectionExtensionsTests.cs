using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

public sealed class SqliteServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaskSqlite_registers_the_connection_factory()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite("Data Source=test.db");

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<ISqlite>());
    }

    [Fact]
    public void AddRaskSqlite_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite("Data Source=first.db");
        services.AddRaskSqlite("Data Source=second.db");

        Assert.Single(services, d => d.ServiceType == typeof(ISqlite));
    }

    [Fact]
    public void AddRaskSqlite_rejects_null_services()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddRaskSqlite("Data Source=test.db"));
    }

    [Fact]
    public void AddRaskSqlite_rejects_empty_connection_string()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddRaskSqlite(string.Empty));
    }

    [Fact]
    public void AddRaskSqlite_runs_configure_and_validates()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRaskSqlite("Data Source=test.db", p => p.BusyTimeout = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AddRaskSqlite_registers_the_retry_options()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite("Data Source=test.db");

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<SqliteBusyRetryOptions>());
    }

    [Fact]
    public void AddRaskSqlite_runs_configureRetry()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite(
            "Data Source=test.db",
            o => { o.Retry.Enabled = true; o.Retry.Timeout = TimeSpan.FromSeconds(12); });

        using var provider = services.BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(12), provider.GetRequiredService<SqliteBusyRetryOptions>().Timeout);
    }

    [Fact]
    public void AddRaskSqlite_validates_configureRetry()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRaskSqlite(
                "Data Source=test.db",
                o => { o.Retry.Enabled = true; o.Retry.PollInterval = TimeSpan.Zero; }));
    }
}
