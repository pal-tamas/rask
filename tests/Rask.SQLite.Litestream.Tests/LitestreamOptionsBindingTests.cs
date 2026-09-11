using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Litestream.Tests;

// LitestreamOptions come from Rask:Litestream and then the callback — which is how a deploy turns backup on with one
// environment variable — and the database they replicate is the app's own unless something says otherwise.
public sealed class LitestreamOptionsBindingTests
{
    [Fact]
    public void The_Rask_Litestream_section_sets_the_replica_and_the_verification_schedule()
    {
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
            ["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app",
            ["Rask:Litestream:Verification:Enabled"] = "true",
            ["Rask:Litestream:Verification:Interval"] = "12:00:00",
        });

        var options = provider.GetRequiredService<LitestreamOptions>();

        Assert.Equal("s3://bucket/app", options.ReplicaUrl);
        Assert.True(options.Verification.Enabled);
        Assert.Equal(TimeSpan.FromHours(12), options.Verification.Interval);
    }

    [Fact]
    public void The_database_defaults_to_the_apps_own()
    {
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
            ["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app",
        });

        Assert.Equal("/data/app.db", provider.GetRequiredService<LitestreamOptions>().DatabasePath);
    }

    [Fact]
    public void A_path_set_in_code_wins_over_the_derived_one()
    {
        using var provider = Provider(
            new()
            {
                ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
                ["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app",
            },
            o => o.DatabasePath = "/data/other.db");

        Assert.Equal("/data/other.db", provider.GetRequiredService<LitestreamOptions>().DatabasePath);
    }

    [Fact]
    public void An_app_database_that_is_not_SQLite_is_reported_as_a_missing_path_under_the_section()
    {
        // Nothing to derive from a Postgres connection string. The failure has to name the setting to fix, not surface
        // the SQLite parser's "Keyword not supported: 'host'" from inside a PostConfigure.
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Host=db;Username=app;Password=not-in-the-message",
            ["Rask:Litestream:ReplicaUrl"] = "s3://bucket/app",
        });

        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<LitestreamOptions>());

        Assert.Contains("Rask:Litestream", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(LitestreamOptions.DatabasePath), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-in-the-message", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_top_level_replica_key_is_not_read()
    {
        using var provider = Provider(new()
        {
            ["Rask:ConnectionStrings:App"] = "Data Source=/data/app.db",
            ["Litestream:ReplicaUrl"] = "s3://bucket/app",
        });

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<LitestreamOptions>());
    }

    private static ServiceProvider Provider(
        Dictionary<string, string?> settings, Action<LitestreamOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskSqliteLitestream(configure);
        return services.BuildServiceProvider();
    }
}
