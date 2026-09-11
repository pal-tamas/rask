using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Snapshots.Tests;

// SqliteSnapshotOptions come from Rask:Snapshots and then the callback; the database they snapshot is the app's own —
// the file behind Rask:ConnectionStrings:App — unless something says otherwise.
public sealed class SqliteSnapshotsOptionsBindingTests
{
    [Fact]
    public void The_Rask_Snapshots_section_sets_the_schedule_and_the_destination()
    {
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
            ["Rask:Snapshots:DestinationDirectory"] = "/backups",
            ["Rask:Snapshots:Interval"] = "02:00:00",
            ["Rask:Snapshots:Retain"] = "3",
        });

        var options = provider.GetRequiredService<SqliteSnapshotOptions>();

        Assert.Equal("/backups", options.DestinationDirectory);
        Assert.Equal(TimeSpan.FromHours(2), options.Interval);
        Assert.Equal(3, options.Retain);
    }

    [Fact]
    public void The_database_defaults_to_the_apps_own()
    {
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
            ["Rask:Snapshots:DestinationDirectory"] = "/backups",
        });

        Assert.Equal("/data/app.db", provider.GetRequiredService<SqliteSnapshotOptions>().DatabasePath);
        Assert.IsType<DirectorySnapshotStore>(provider.GetRequiredService<ISqliteSnapshotStore>());
    }

    [Fact]
    public void A_path_set_in_code_wins_over_the_derived_one()
    {
        using var provider = Provider(
            new()
            {
                ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
                ["Rask:Snapshots:DestinationDirectory"] = "/backups",
            },
            o => o.DatabasePath = "/data/other.db");

        Assert.Equal("/data/other.db", provider.GetRequiredService<SqliteSnapshotOptions>().DatabasePath);
    }

    [Fact]
    public void An_app_database_that_is_not_SQLite_is_reported_as_a_missing_path_under_the_section()
    {
        // Nothing to derive from a Postgres connection string. The failure has to name the setting to fix, not surface
        // the SQLite parser's "Keyword not supported: 'host'" from inside a PostConfigure.
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Host=db;Username=app;Password=not-in-the-message",
            ["Rask:Snapshots:DestinationDirectory"] = "/backups",
        });

        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<SqliteSnapshotOptions>());

        Assert.Contains("Rask:Snapshots", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SqliteSnapshotOptions.DatabasePath), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-in-the-message", error.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Provider(
        Dictionary<string, string?> settings, Action<SqliteSnapshotOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskSqliteSnapshots(configure);
        return services.BuildServiceProvider();
    }
}
