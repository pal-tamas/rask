using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Rask.Jobs.Tests;

// JobsOptions come from Rask:Jobs first and the AddRaskJobs callback second. The recurring schedule is the one
// member appsettings cannot express — the binding generator reports that, and Rask.Jobs.csproj suppresses it —
// so these tests are what notice if some other property silently stops binding.
public sealed class RaskJobsOptionsBindingTests
{
    private static readonly Dictionary<string, string?> EverySetting = new()
    {
        ["Rask:Jobs:PollInterval"] = "00:00:07",
        ["Rask:Jobs:BatchSize"] = "42",
        ["Rask:Jobs:LeaseDuration"] = "00:03:00",
        ["Rask:Jobs:MaxAttempts"] = "9",
        ["Rask:Jobs:BaseRetryDelay"] = "00:00:11",
        ["Rask:Jobs:MaxRetryDelay"] = "00:30:00",
        ["Rask:Jobs:RetentionPeriod"] = "2.00:00:00",
        ["Rask:Jobs:ShutdownGracePeriod"] = "00:00:03",
        ["Rask:Jobs:TimeZone"] = "Europe/Budapest",
    };

    [Fact]
    public void Every_setting_binds_from_the_Rask_Jobs_section()
    {
        using var provider = Provider(EverySetting);

        var options = provider.GetRequiredService<JobsOptions>();

        Assert.Equal(TimeSpan.FromSeconds(7), options.PollInterval);
        Assert.Equal(42, options.BatchSize);
        Assert.Equal(TimeSpan.FromMinutes(3), options.LeaseDuration);
        Assert.Equal(9, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(11), options.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(30), options.MaxRetryDelay);
        Assert.Equal(TimeSpan.FromDays(2), options.RetentionPeriod);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ShutdownGracePeriod);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest"), options.TimeZone);
    }

    [Fact]
    public void A_time_zone_this_machine_does_not_have_fails_the_boot_naming_the_key()
    {
        // The options plumbing wraps it, so what an operator sees is the message, not the type.
        var error = Assert.Throws<OptionsValidationException>(() =>
            Provider(new Dictionary<string, string?> { ["Rask:Jobs:TimeZone"] = "Mars/Olympus_Mons" })
                .GetRequiredService<JobsOptions>());

        Assert.Contains("Rask:Jobs:TimeZone", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_settable_property_is_covered_by_the_binding_test()
    {
        // A property the generator cannot bind is skipped without failing the build, so a new one has to be
        // added to EverySetting (and asserted above) before this passes.
        var settable = typeof(JobsOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);
        var covered = EverySetting.Keys
            .Select(k => k["Rask:Jobs:".Length..])
            .Order(StringComparer.Ordinal);

        Assert.Equal(covered, settable);
    }

    [Fact]
    public void The_callback_wins_over_configuration()
    {
        using var provider = Provider(
            new() { ["Rask:Jobs:MaxAttempts"] = "9", ["Rask:Jobs:BatchSize"] = "42" },
            o => o.MaxAttempts = 3);

        var options = provider.GetRequiredService<JobsOptions>();

        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(42, options.BatchSize);
    }

    [Fact]
    public void A_recurring_job_added_in_code_survives_binding()
    {
        using var provider = Provider(
            new() { ["Rask:Jobs:MaxAttempts"] = "9" },
            o => o.Run(() => new TickJob()).Named("tick").Every(TimeSpan.FromHours(1)));

        var options = provider.GetRequiredService<JobsOptions>();

        Assert.Single(options.RecurringJobs);
        Assert.Equal(9, options.MaxAttempts);
    }

    [Fact]
    public void An_invalid_configured_value_is_rejected_naming_the_section()
    {
        using var provider = Provider(new() { ["Rask:Jobs:BatchSize"] = "0" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<JobsOptions>());
        Assert.Contains("Rask:Jobs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_top_level_Jobs_section_is_not_read()
    {
        using var provider = Provider(new() { ["Jobs:MaxAttempts"] = "9" });

        Assert.Equal(25, provider.GetRequiredService<JobsOptions>().MaxAttempts);
    }

    [Fact]
    public void Without_configuration_the_defaults_apply()
    {
        var services = new ServiceCollection();
        services.AddRaskJobs<NoDb>();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(25, provider.GetRequiredService<JobsOptions>().MaxAttempts);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, Action<JobsOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRaskJobs<NoDb>(configure);
        return services.BuildServiceProvider();
    }

    // Only a type argument: resolving JobsOptions never builds a context.
    private sealed class NoDb(DbContextOptions<NoDb> options) : DbContext(options);
}
