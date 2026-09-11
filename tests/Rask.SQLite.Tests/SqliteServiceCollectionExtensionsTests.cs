using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.SQLite.Tests;

// AddRaskSqlite takes its connection string from Rask:ConnectionStrings:App and its pragmas from Rask:Sqlite, then the
// callback. Both are read when the container resolves them, so a bad value or a missing key surfaces there — at host
// start in a real app — naming the key to fix.
public sealed class SqliteServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaskSqlite_registers_the_connection_factory()
    {
        using var provider = Build(App("Data Source=test.db"));

        Assert.NotNull(provider.GetService<ISqlite>());
    }

    [Fact]
    public void AddRaskSqlite_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite(o => o.CacheSize = 1);
        services.AddRaskSqlite(o => o.CacheSize = 2);

        Assert.Single(services, d => d.ServiceType == typeof(ISqlite));
    }

    [Fact]
    public void AddRaskSqlite_rejects_null_services()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddRaskSqlite());
    }

    [Fact]
    public void AddRaskSqlite_names_the_connection_string_it_could_not_find()
    {
        using var provider = Build([]);

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISqlite>());
        Assert.Contains("Rask:ConnectionStrings:App", error.Message, StringComparison.Ordinal);
        Assert.Contains("Rask__ConnectionStrings__App", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_connection_string_is_not_read()
    {
        using var provider = Build(new() { ["ConnectionStrings:App"] = "Data Source=test.db" });

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISqlite>());
    }

    [Fact]
    public void AddRaskSqlite_runs_configure_and_validates()
    {
        using var provider = Build(App("Data Source=test.db"), p => p.BusyTimeout = TimeSpan.FromSeconds(-1));

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<SqliteOptions>());
        Assert.Contains("Rask:Sqlite", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddRaskSqlite_registers_the_retry_options()
    {
        using var provider = Build(App("Data Source=test.db"));

        Assert.NotNull(provider.GetService<SqliteBusyRetryOptions>());
    }

    [Fact]
    public void AddRaskSqlite_runs_configureRetry()
    {
        using var provider = Build(
            App("Data Source=test.db"),
            o => { o.Retry.Enabled = true; o.Retry.Timeout = TimeSpan.FromSeconds(12); });

        Assert.Equal(TimeSpan.FromSeconds(12), provider.GetRequiredService<SqliteBusyRetryOptions>().Timeout);
    }

    [Fact]
    public void AddRaskSqlite_validates_configureRetry()
    {
        using var provider = Build(
            App("Data Source=test.db"),
            o => { o.Retry.Enabled = true; o.Retry.PollInterval = TimeSpan.Zero; });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<SqliteOptions>());
    }

    [Fact]
    public void The_Rask_Sqlite_section_sets_the_pragmas_and_the_retry()
    {
        var settings = App("Data Source=test.db");
        settings["Rask:Sqlite:StrictTables"] = "true";
        settings["Rask:Sqlite:BusyTimeout"] = "00:00:09";
        settings["Rask:Sqlite:Retry:Enabled"] = "true";
        settings["Rask:Sqlite:Retry:Timeout"] = "00:00:12";
        using var provider = Build(settings);

        var options = provider.GetRequiredService<SqliteOptions>();

        Assert.True(options.StrictTables);
        Assert.Equal(TimeSpan.FromSeconds(9), options.BusyTimeout);
        Assert.True(provider.GetRequiredService<SqliteBusyRetryOptions>().Enabled);
        Assert.Equal(TimeSpan.FromSeconds(12), provider.GetRequiredService<SqliteBusyRetryOptions>().Timeout);
    }

    [Fact]
    public void The_callback_wins_over_the_section()
    {
        var settings = App("Data Source=test.db");
        settings["Rask:Sqlite:CacheSize"] = "500";
        using var provider = Build(settings, o => o.CacheSize = 900);

        Assert.Equal(900, provider.GetRequiredService<SqliteOptions>().CacheSize);
    }

    private static Dictionary<string, string?> App(string connectionString) =>
        new() { ["Rask:ConnectionStrings:App"] = connectionString };

    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<SqliteOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskSqlite(configure);
        return services.BuildServiceProvider();
    }
}
