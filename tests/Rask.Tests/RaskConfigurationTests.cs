using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rask.Mail;
using Rask.WebPush;

namespace Rask.Tests;

/// <summary>
///     Where a <c>RaskApp</c>'s settings come from: its own development defaults, then appsettings (and the
///     environment), then code — each battery reading its own <c>Rask:&lt;Area&gt;</c> section.
/// </summary>
public sealed class RaskConfigurationTests
{
    private sealed class ConfiguredDbContext(DbContextOptions<ConfiguredDbContext> options) : DbContext(options);

    [Fact]
    public void A_battery_gets_a_default_that_boots_when_nothing_is_configured()
    {
        var built = Build();

        Assert.Equal("no-reply@example.com", built.Services.GetRequiredService<MailOptions>().From);
    }

    [Fact]
    public void Configuration_beats_the_default()
    {
        var built = Build(settings: new() { ["Rask:Mail:From"] = "from-config@example.test" });

        Assert.Equal("from-config@example.test", built.Services.GetRequiredService<MailOptions>().From);
    }

    [Fact]
    public void Code_beats_configuration()
    {
        var built = Build(
            settings: new() { ["Rask:Mail:From"] = "from-config@example.test" },
            arrange: app => app.Configure(c => c.Mail.Configure(o => o.From = "from-code@example.test")));

        Assert.Equal("from-code@example.test", built.Services.GetRequiredService<MailOptions>().From);
    }

    [Fact]
    public void The_app_database_is_strict_by_default()
    {
        Assert.IsType<Rask.SQLite.RaskSqliteStrictRangeExclusionSqlGenerator>(AppMigrationsSqlGenerator(settings: null));
    }

    [Fact]
    public void A_setting_the_defaults_carry_can_still_be_turned_off_in_configuration()
    {
        // What UseRaskSqlite(sp) actually applied, not what IConfiguration says. A later source always wins that lookup,
        // so reading the key back would pass even if the wiring went back to forcing StrictTables on in code.
        Assert.IsType<Rask.SQLite.RaskSqliteRangeExclusionSqlGenerator>(
            AppMigrationsSqlGenerator(new() { ["Rask:Sqlite:StrictTables"] = "false" }));
    }

    [Fact]
    public void Request_validation_can_be_turned_off_in_configuration_while_the_battery_is_on()
    {
        // The wiring used to assign ValidateRequests from the battery in a callback, and a callback is code, which
        // beats configuration — so the setting was read and then silently overwritten.
        Assert.True(HasValidationBehavior(settings: null));
        Assert.False(HasValidationBehavior(new() { ["Rask:Cqrs:ValidateRequests"] = "false" }));
    }

    private static Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator AppMigrationsSqlGenerator(
        Dictionary<string, string?>? settings)
    {
        // No DbContext of the app's own, so RaskAppDbContext is the context wired through UseRaskSqlite(sp).
        // Creating a context does not open the database, so nothing is written to disk.
        var built = CreateApp(settings).Build<TestApp>();
        using var db = built.Services.GetRequiredService<IDbContextFactory<RaskAppDbContext>>().CreateDbContext();
        return Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator>(db);
    }

    private static bool HasValidationBehavior(Dictionary<string, string?>? settings)
    {
        var app = CreateApp(settings);
        app.Services.AddDbContextFactory<ConfiguredDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        app.Build<TestApp>();

        return app.Services.Any(d => d.ServiceType == typeof(Rask.Cqrs.IPipelineBehavior<,>)
            && d.ImplementationType is { Name: "ValidationBehavior`2" });
    }

    private static RaskApp CreateApp(Dictionary<string, string?>? settings) =>
        RaskApp.Create([], b =>
        {
            b.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            if (settings is not null)
            {
                b.Configuration.AddInMemoryCollection(settings);
            }
        });

    [Fact]
    public void A_connection_string_set_in_code_beats_the_configured_one()
    {
        var built = Build(
            settings: new() { ["Rask:ConnectionStrings:App"] = "Data Source=from-config.db" },
            arrange: app => app.Configure(c => c.ConnectionString = "Data Source=from-code.db"));

        Assert.Equal(
            "Data Source=from-code.db",
            built.Services.GetRequiredService<IConfiguration>()["Rask:ConnectionStrings:App"]);
    }

    [Fact]
    public void Web_push_keys_in_configuration_reach_the_sender()
    {
        // The regression: the keys used to be enough to switch Web Push on, but were never copied into its options,
        // so an app that configured push the documented way crashed at startup.
        var keys = VapidKeys.Generate();
        var built = Build(settings: new()
        {
            ["Rask:WebPush:VapidKeys:PublicKey"] = keys.PublicKey,
            ["Rask:WebPush:VapidKeys:PrivateKey"] = keys.PrivateKey,
            ["Rask:WebPush:Subject"] = "mailto:ops@example.test",
        });

        var options = built.Services.GetRequiredService<WebPushOptions>();

        Assert.Equal(keys.PublicKey, options.VapidKeys!.PublicKey);
        Assert.Equal("mailto:ops@example.test", options.Subject);
        Assert.NotNull(built.Services.GetService<Rask.WebPush.IWebPush>());
    }

    [Fact]
    public void Web_push_keys_without_a_subject_are_refused_naming_it()
    {
        var keys = VapidKeys.Generate();
        var built = Build(settings: new()
        {
            ["Rask:WebPush:VapidKeys:PublicKey"] = keys.PublicKey,
            ["Rask:WebPush:VapidKeys:PrivateKey"] = keys.PrivateKey,
        });

        var error = Assert.Throws<OptionsValidationException>(() => built.Services.GetRequiredService<WebPushOptions>());
        Assert.Contains("Subject", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_top_level_web_push_keys_do_not_switch_push_on()
    {
        var keys = VapidKeys.Generate();
        var built = Build(settings: new()
        {
            ["WebPush:PublicKey"] = keys.PublicKey,
            ["WebPush:PrivateKey"] = keys.PrivateKey,
        });

        Assert.Null(built.Services.GetService<WebPushOptions>());
    }

    private static WebApplication Build(Dictionary<string, string?>? settings = null, Action<RaskApp>? arrange = null)
    {
        var app = CreateApp(settings);
        arrange?.Invoke(app);
        app.Services.AddDbContextFactory<ConfiguredDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        return app.Build<TestApp>();
    }
}
