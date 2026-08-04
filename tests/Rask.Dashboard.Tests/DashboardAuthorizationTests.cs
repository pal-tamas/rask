using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    [Fact]
    public async Task AllowAnonymousAccess_opens_it_deliberately_outside_development()
    {
        await using var h = new DashboardHarness(
            environment: Environments.Production,
            configure: o => o.AllowAnonymousAccess = true);

        Assert.True(await AuthorizeAsync(h, Anonymous()));
        Assert.True(h.Get<DashboardSecurityState>().IsUnsecured);
    }

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
