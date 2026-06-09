using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

#pragma warning disable RASK014 // test harness instantiates StubComponent directly

namespace Rask.Core.Tests.Components;

// The headless Authorize component selects exactly one of three slots (Authorized / NotAuthorized /
// Authorizing) off Component.User, plus optional role/policy gating. These pin that selection. The
// gate is built INSIDE a render delegate so its generated factory runs under a live render context
// (which fires OnMount/OnPropsChangedAsync) — building it eagerly would skip the lifecycle.
public class AuthorizeTests
{
    [Fact]
    public void Anonymous_WithoutNotAuthorized_RendersNothing()
    {
        var html = Render(Anonymous(), () => Authorize(Authorized: Span()["AUTHED"]));

        Assert.DoesNotContain("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void Anonymous_RendersNotAuthorizedSlot()
    {
        var html = Render(Anonymous(),
            () => Authorize(Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]));

        Assert.Contains("DENIED", html);
        Assert.DoesNotContain("AUTHED", html);
    }

    [Fact]
    public void Authenticated_RendersAuthorizedSlot()
    {
        var html = Render(User("alice"),
            () => Authorize(Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]));

        Assert.Contains("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void Authenticated_NoAuthorizedSlot_RendersChildrenShorthand()
    {
        // Authorize(Roles: "...")[ content ] — the children indexer is the authorized branch.
        var html = Render(User("alice"), () => Authorize()[Span()["CHILD"]]);

        Assert.Contains("CHILD", html);
    }

    [Fact]
    public void RoleMatch_RendersAuthorized()
    {
        var html = Render(User("root", "admin"),
            () => Authorize(Roles: ["admin"], Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]));

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void RoleMiss_RendersNotAuthorized()
    {
        var html = Render(User("alice", "user"),
            () => Authorize(Roles: ["admin"], Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]));

        Assert.Contains("DENIED", html);
        Assert.DoesNotContain("AUTHED", html);
    }

    [Fact]
    public void AnyOfRoles_MatchesOnEither()
    {
        var html = Render(User("alice", "editor"),
            () => Authorize(Roles: ["admin", "editor"], Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]));

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void ProviderLoading_RendersAuthorizingSlot()
    {
        var html = Render(new LoadingUser(),
            () => Authorize(Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"], Authorizing: Span()["LOADING"]));

        Assert.Contains("LOADING", html);
        Assert.DoesNotContain("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void PolicyAllow_RendersAuthorized()
    {
        var html = Render(User("root", "admin"),
            () => Authorize(Policy: "admins-only", Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]),
            WithAdminsPolicy);

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void PolicyDeny_RendersNotAuthorized()
    {
        var html = Render(User("alice", "user"),
            () => Authorize(Policy: "admins-only", Authorized: Span()["AUTHED"], NotAuthorized: Span()["DENIED"]),
            WithAdminsPolicy);

        Assert.Contains("DENIED", html);
        Assert.DoesNotContain("AUTHED", html);
    }

    // Register the policy provider (real) but override the authorization service with a synchronous
    // role evaluator, so the policy verdict resolves within the single test render frame. (The real
    // DefaultAuthorizationService completes on a continuation; in a live session that triggers a
    // re-render, but a one-shot RenderAsLiveRoot would only capture the pre-resolution frame.)
    private static void WithAdminsPolicy(IServiceCollection services)
    {
        services.AddAuthorizationCore(o => o.AddPolicy("admins-only", p => p.RequireRole("admin")));
        services.AddSingleton<IAuthorizationService>(new SyncRoleAuthz());
    }

    private static string Render(IUserProvider provider, Func<Component> gate, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddSingleton(provider);
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return new StubComponent(gate).RenderAsLiveRoot(sp);
    }

    private static IUserProvider Anonymous() => new FixedUser(new ClaimsPrincipal(new ClaimsIdentity()));

    private static IUserProvider User(string name, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        return new FixedUser(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }

    private sealed class FixedUser(ClaimsPrincipal principal) : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = principal;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class LoadingUser : IUserProvider
    {
        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity());
        public bool IsLoading => true;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    // Synchronous role-requirement evaluator — faithful to RequireRole policies but completes inline.
    private sealed class SyncRoleAuthz : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        {
            foreach (var req in requirements)
            {
                if (req is Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement roles
                    && !roles.AllowedRoles.Any(user.IsInRole))
                {
                    return Task.FromResult(AuthorizationResult.Failed());
                }
            }

            return Task.FromResult(AuthorizationResult.Success());
        }

        // Not used by Authorize (it resolves the policy via IAuthorizationPolicyProvider first).
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            throw new NotSupportedException();
    }
}
