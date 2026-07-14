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
        Assert.NotNull(provider.GetService<IRaskSqliteConnectionFactory>());
    }

    [Fact]
    public void AddRaskSqlite_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite("Data Source=first.db");
        services.AddRaskSqlite("Data Source=second.db");

        Assert.Single(services, d => d.ServiceType == typeof(IRaskSqliteConnectionFactory));
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
}
