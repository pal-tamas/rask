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
    public async Task EmptyChain_ReturnsAllow()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(sp, Array.Empty<Type>(), Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task ChainWithoutAttributes_ReturnsAllow()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(PublicPage) }, Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task AnonymousUser_ProtectedPage_ReturnsChallenge()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage) }, Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Challenge, result.Outcome);
        Assert.Equal(typeof(ProtectedPage), result.FailedOnPage);
    }

    [Fact]
    public async Task AuthenticatedUser_ProtectedPage_ReturnsAllow()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage) }, Authenticated());
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task AuthenticatedUser_RoleMismatch_ReturnsForbid()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(AdminOnlyPage) }, Authenticated());
        Assert.Equal(RouteAuthorizationOutcome.Forbid, result.Outcome);
    }

    [Fact]
    public async Task AuthenticatedUser_RoleMatch_ReturnsAllow()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(AdminOnlyPage) }, Authenticated(new Claim(ClaimTypes.Role, "Admin")));
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task AllowAnonymous_OverridesAuthorize()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(OpenPage) }, Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task ParentAuthorize_ChildAllowAnonymous_ReturnsAllow()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(ProtectedPage), typeof(OpenPage) }, Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Allow, result.Outcome);
    }

    [Fact]
    public async Task ParentAllowAnonymous_ChildAuthorize_AnonymousUser_ReturnsChallenge()
    {
        var sp = BuildServices();
        var result = await RouteAuthorizationGuard.EvaluateAsync(
            sp, new[] { typeof(OpenPage), typeof(ProtectedPage) }, Anonymous());
        Assert.Equal(RouteAuthorizationOutcome.Challenge, result.Outcome);
    }

    private sealed class PublicPage : Component
    {
        public override Component Render() => this;
    }

    [Authorize]
    private sealed class ProtectedPage : Component
    {
        public override Component Render() => this;
    }

    [Authorize(Roles = "Admin")]
    private sealed class AdminOnlyPage : Component
    {
        public override Component Render() => this;
    }

    [AllowAnonymous]
    [Authorize]
    private sealed class OpenPage : Component
    {
        public override Component Render() => this;
    }
}
