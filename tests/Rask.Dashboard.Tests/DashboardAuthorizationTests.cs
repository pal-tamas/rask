using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The dashboard exposes job payloads, stored email bodies and log lines, so the question "who can reach
/// it if I configure nothing?" has exactly one acceptable answer outside Development: nobody.
/// </summary>
public sealed class DashboardAuthorizationTests
{
    [Fact]
    public async Task With_no_policy_configured_production_denies_everyone()
    {
        await using var h = new DashboardHarness(environment: Environments.Production);

        Assert.False(await AuthorizeAsync(h, Anonymous()));
        Assert.False(await AuthorizeAsync(h, SignedIn("admin")));   // even a real user
    }

    [Fact]
    public async Task With_no_policy_configured_development_allows_everyone()
    {
        await using var h = new DashboardHarness(environment: Environments.Development);

        Assert.True(await AuthorizeAsync(h, Anonymous()));
        Assert.True(h.Get<DashboardSecurityState>().IsUnsecured);   // and says so in the UI
    }

    [Fact]
    public async Task An_application_policy_wins_over_the_fallback()
    {
        // Registered BEFORE AddRaskDashboard, which is the order that would break a naive
        // Configure-based default — PostConfigure is what makes the order irrelevant.
        await using var h = new DashboardHarness(
            environment: Environments.Development,
            extra: services => services.AddAuthorizationCore(o =>
                o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Ops"))));

        Assert.False(await AuthorizeAsync(h, SignedIn("admin")));
        Assert.True(await AuthorizeAsync(h, SignedIn("admin", role: "Ops")));

        // The app configured access, so the "unsecured" banner must not appear even in Development.
        Assert.False(h.Get<DashboardSecurityState>().IsUnsecured);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public async Task AllowAnonymousAccess_opens_nothing_outside_development_and_an_unnamed_environment_is_production(string environment)
    {
        await using var h = new DashboardHarness(
            environment: environment,
            configure: o => o.AllowAnonymousAccess = true);

        Assert.False(await AuthorizeAsync(h, Anonymous()));
        Assert.False(await AuthorizeAsync(h, SignedIn("admin")));
        Assert.False(h.Get<DashboardSecurityState>().IsUnsecured);
    }

    [Fact]
    public async Task AllowAnonymousAccess_from_configuration_opens_nothing_in_production_either()
    {
        // Rask:Ops is read like every other section, so this switch is reachable from an environment variable
        // (Rask__Ops__AllowAnonymousAccess) — which is exactly why it cannot be what opens the console.
        await using var h = new DashboardHarness(
            environment: Environments.Production,
            extra: services => services.AddSingleton(Configuration("true")));

        Assert.True(h.Get<OpsOptions>().AllowAnonymousAccess);
        Assert.False(await AuthorizeAsync(h, Anonymous()));
    }

    [Fact]
    public async Task Code_that_turns_the_setting_off_wins_over_configuration_and_is_not_warned_about()
    {
        var logged = new RecordedWarnings();
        await using var h = new DashboardHarness(
            environment: Environments.Production,
            configure: o => o.AllowAnonymousAccess = false,
            extra: services => services.AddSingleton(Configuration("true")).AddSingleton<ILoggerProvider>(logged));

        await StartAsync(h);

        Assert.False(h.Get<OpsOptions>().AllowAnonymousAccess);
        Assert.False(await AuthorizeAsync(h, Anonymous()));
        Assert.Empty(logged.Messages);
    }

    [Fact]
    public async Task In_production_the_applications_policy_still_decides_with_the_setting_on()
    {
        await using var h = new DashboardHarness(
            environment: Environments.Production,
            configure: o => o.AllowAnonymousAccess = true,
            extra: services => services.AddAuthorizationCore(o =>
                o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin"))));

        Assert.False(await AuthorizeAsync(h, Anonymous()));
        Assert.False(await AuthorizeAsync(h, SignedIn("clerk")));
        Assert.True(await AuthorizeAsync(h, SignedIn("admin", role: "Admin")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Development_is_open_with_the_setting_on_or_off(bool allowAnonymousAccess)
    {
        await using var h = new DashboardHarness(
            environment: Environments.Development,
            configure: o => o.AllowAnonymousAccess = allowAnonymousAccess);

        Assert.True(await AuthorizeAsync(h, Anonymous()));
        Assert.True(h.Get<DashboardSecurityState>().IsUnsecured);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")]
    public async Task A_host_that_sets_it_outside_development_is_warned_once_at_startup_that_it_is_ignored(string environment)
    {
        var logged = new RecordedWarnings();
        await using var h = new DashboardHarness(
            environment: environment,
            configure: o => o.AllowAnonymousAccess = true,
            extra: services => services.AddSingleton<ILoggerProvider>(logged));

        await StartAsync(h);

        var warning = Assert.Single(logged.Messages);
        Assert.Contains("Rask:Ops:AllowAnonymousAccess is set and is ignored", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task No_warning_is_logged_in_development_or_with_the_setting_off(string environment, bool allowAnonymousAccess)
    {
        var logged = new RecordedWarnings();
        await using var h = new DashboardHarness(
            environment: environment,
            configure: o => o.AllowAnonymousAccess = allowAnonymousAccess,
            extra: services => services.AddSingleton<ILoggerProvider>(logged));

        await StartAsync(h);

        Assert.Empty(logged.Messages);
    }

    // What a host does as it starts: every hosted service, once.
    private static async Task StartAsync(DashboardHarness harness)
    {
        foreach (var service in harness.Services.GetServices<IHostedService>())
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    // The warnings the dashboard itself logged, whichever category they were written under.
    private sealed class RecordedWarnings : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("Rask.Dashboard", StringComparison.Ordinal) ? new Recorder(Messages) : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Recorder(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }

    private static Microsoft.Extensions.Configuration.IConfiguration Configuration(string allowAnonymousAccess) =>
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rask:Ops:AllowAnonymousAccess"] = allowAnonymousAccess,
            })
            .Build();

    [Fact]
    public async Task The_layout_carries_the_policy_so_every_page_inherits_it()
    {
        await using var h = new DashboardHarness();

        // RouteAuthorizationGuard walks the whole route chain, so one [Authorize] on the layout covers
        // every child page — including pages added later, which is the point of putting it there.
        var attribute = typeof(Pages.DashboardLayout)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(RaskDashboardPolicies.Access, attribute.Policy);

        var children = new[]
        {
            typeof(Pages.OverviewPage), typeof(Pages.QueuePage), typeof(Pages.CachePage),
            typeof(Pages.LogsPage), typeof(Pages.SystemPage),
        };

        // None of them may carry [AllowAnonymous] — that would punch a hole straight through the chain.
        Assert.All(children, page =>
            Assert.Empty(page.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)));
    }

    private static async Task<bool> AuthorizeAsync(DashboardHarness harness, ClaimsPrincipal user)
    {
        var authorization = harness.Services.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(user, resource: null, RaskDashboardPolicies.Access);
        return result.Succeeded;
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn(string name, string? role = null)
    {
        List<Claim> claims = [new(ClaimTypes.Name, name)];
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }
}
