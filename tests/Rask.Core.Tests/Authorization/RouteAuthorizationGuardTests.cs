using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authorization;

namespace Rask.Core.Tests.Authorization;

public class RouteAuthorizationGuardTests
{
    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "alice") };
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role));
    }

    [Fact]
    public async Task An_empty_page_chain_is_allowed()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(sp, Array.Empty<Type>(), Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task A_chain_without_authorize_attributes_is_allowed()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(PublicPage) }, Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task An_anonymous_user_on_a_protected_page_is_challenged()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage) }, Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Challenge, result.Outcome);
        Assert.Equal(typeof(ProtectedPage), result.FailedOnPage);
    }

    [Fact]
    public async Task An_authenticated_user_on_a_protected_page_is_allowed()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage) }, Authenticated());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task An_authenticated_user_without_the_role_is_forbidden()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(AdminOnlyPage) }, Authenticated());

        Assert.Equal(RouteAuthorizationOutcome.Forbid, result.Outcome);
    }

    [Fact]
    public async Task An_authenticated_user_with_the_role_is_allowed()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(AdminOnlyPage) }, Authenticated(new Claim(ClaimTypes.Role, "Admin")));

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task AllowAnonymous_overrides_Authorize_on_the_same_page()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(OpenPage) }, Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task An_AllowAnonymous_child_under_an_Authorize_parent_is_allowed()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage), typeof(OpenPage) }, Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task An_anonymous_user_on_an_Authorize_child_under_an_AllowAnonymous_parent_is_challenged()
    {
        var sp = BuildServices();

        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(OpenPage), typeof(ProtectedPage) }, Anonymous());

        Assert.Equal(RouteAuthorizationOutcome.Challenge, result.Outcome);
    }

    private sealed class PublicPage : Component
    {
        protected override Component? Render() => this;
    }

    [Authorize]
    private sealed class ProtectedPage : Component
    {
        protected override Component? Render() => this;
    }

    [Authorize(Roles = "Admin")]
    private sealed class AdminOnlyPage : Component
    {
        protected override Component? Render() => this;
    }

    [AllowAnonymous]
    [Authorize]
    private sealed class OpenPage : Component
    {
        protected override Component? Render() => this;
    }
}
