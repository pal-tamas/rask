using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.SQLite;

namespace Rask.Logging.Tests;

/// <summary>
/// The options come from <c>Rask:Logs</c> and then the callback, and a bad value fails when they are built — at host
/// start in a real app, where the message names the key — not hours later when the first flush tears the host down.
/// </summary>
public sealed class LogsOptionsTests
{
    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_options_are_rejected_when_they_are_built(Action<LogsOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddRaskLogging(configure);
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<LogsOptions>());

        Assert.Contains("Rask:Logs", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<Action<LogsOptions>> InvalidOptions() => new()
    {
        o => o.Retention = TimeSpan.FromDays(-1),
        o => o.MaxRows = -1,
        o => o.FlushInterval = TimeSpan.Zero,
        o => o.BatchSize = 0,
        o => o.QueueCapacity = 0,
        o => o.SweepInterval = TimeSpan.Zero,
        o => o.ShutdownGracePeriod = TimeSpan.FromSeconds(-1),
        o => o.Pragmas.JournalMode = (SqliteJournalMode)99,
    };

    [Fact]
    public void The_error_names_the_connection_string_it_could_not_find()
    {
        var services = new ServiceCollection();
        services.AddRaskLogging();
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ILogs>());

        Assert.Contains("Rask:ConnectionStrings:Logs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Rask_Logging_section_sets_the_options()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rask:Logs:MaxRows"] = "50",
                ["Rask:Logs:MinimumLevel"] = "Warning",
                ["Rask:Logs:ExcludedCategories:0"] = "App.Noise",
            })
            .Build());
        services.AddRaskLogging(o => o.ExcludedCategories.Add("App.Chatter"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<LogsOptions>();

        Assert.Equal(50, options.MaxRows);
        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
        // A list from configuration and one from code add up rather than one replacing the other.
        Assert.True(options.IsExcluded("App.Noise.Poller"));
        Assert.True(options.IsExcluded("App.Chatter.Poller"));
    }

    [Fact]
    public void The_defaults_bound_the_store_by_both_age_and_row_count()
    {
        var options = new LogsOptions();

        // Either limit alone leaves the disk unbounded — age lets a storm fill it inside the window, and a
        // row cap alone can shrink the window to minutes. The defaults set both on purpose.
        Assert.True(options.Retention > TimeSpan.Zero);
        Assert.True(options.MaxRows > 0);
    }

    /// <summary>
    /// Registering twice must not capture every entry twice. <c>TryAddEnumerable</c> keys on the
    /// implementation type, which is what makes a library and its host both calling this safe.
    /// </summary>
    [Fact]
    public void Registering_twice_captures_each_entry_once()
    {
        var services = new ServiceCollection();

        services.AddRaskLogging();
        services.AddRaskLogging();

        Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Single(services, d => d.ServiceType == typeof(ILogs));
    }

    [Fact]
    public void The_stores_own_categories_are_excluded_by_prefix()
    {
        var options = new LogsOptions();

        Assert.True(options.IsExcluded("Rask.Logging"));
        Assert.True(options.IsExcluded("Rask.Logging.LogWriter"));
        Assert.True(options.IsExcluded("Microsoft.Data.Sqlite.Command"));
        Assert.False(options.IsExcluded("Rask.Live"));
        Assert.False(options.IsExcluded("App.Checkout"));
    }

    [Fact]
    public void Configured_prefixes_are_excluded()
    {
        var options = new LogsOptions();
        options.ExcludedCategories.Add("Microsoft.AspNetCore.");

        Assert.True(options.IsExcluded("Microsoft.AspNetCore.Routing.Matcher"));
        Assert.False(options.IsExcluded("Microsoft.Extensions.Hosting"));
    }
}
