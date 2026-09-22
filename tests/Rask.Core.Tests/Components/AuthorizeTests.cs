using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

#pragma warning disable RASK014 // test harness instantiates StubComponent directly

namespace Rask.Core.Tests.Components;

// The headless Authorize component selects exactly one of three slots (Authorized / NotAuthorized /
// Authorizing) off the current user (IUserProvider), plus optional role/policy gating. These pin that selection. The
// gate is built INSIDE a render delegate so its generated factory runs under a live render context
// (which fires Mount/Updated) — building it eagerly would skip the lifecycle.
public partial class AuthorizeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_anonymous_user_sees_nothing_without_a_NotAuthorized_slot()
    {
        var html = Render(Anonymous(), () => Authorize.Authorized(_ => Span["AUTHED"]));

        Assert.DoesNotContain("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void An_anonymous_user_sees_the_NotAuthorized_slot()
    {
        var html = Render(Anonymous(),
            () => Authorize.Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]));

        Assert.Contains("DENIED", html);
        Assert.DoesNotContain("AUTHED", html);
    }

    [Fact]
    public void An_authenticated_user_sees_the_Authorized_slot()
    {
        var html = Render(User("alice"),
            () => Authorize.Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]));

        Assert.Contains("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void Without_an_Authorized_slot_an_authenticated_user_sees_the_children()
    {
        // Authorize(Roles: "...")[ content ] — the children indexer is the authorized branch.
        var html = Render(User("alice"), () => Authorize[Span["CHILD"]]);

        Assert.Contains("CHILD", html);
    }

    [Fact]
    public void The_authorized_delegate_receives_the_current_principal()
    {
        // The delegate form is handed the signed-in principal, so authorized markup can read the user
        // (e.g. a greeting) without injecting IUserProvider or subscribing to Changed.
        var html = Render(User("alice"),
            () => Authorize.Authorized(user => Span[$"Hi {user.Identity!.Name}"]));

        Assert.Contains("Hi alice", html);
    }

    [Fact]
    public void An_anonymous_user_does_not_invoke_the_authorized_delegate()
    {
        // Denied gate must not run the authorized delegate (it would NRE on the anonymous identity).
        var html = Render(Anonymous(),
            () => Authorize
                .Authorized(user => Span[user.Identity!.Name!.ToUpperInvariant()])
                .NotAuthorized(Span["DENIED"]));

        Assert.Contains("DENIED", html);
    }

    [Fact]
    public void A_matching_role_renders_the_authorized_slot()
    {
        var html = Render(User("root", "admin"),
            () => Authorize.Roles(["admin"]).Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]));

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void A_missing_role_renders_the_not_authorized_slot()
    {
        var html = Render(User("alice", "user"),
            () => Authorize.Roles(["admin"]).Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]));

        Assert.Contains("DENIED", html);
        Assert.DoesNotContain("AUTHED", html);
    }

    [Fact]
    public void Any_of_several_roles_matches_on_either()
    {
        var html = Render(User("alice", "editor"),
            () => Authorize.Roles(["admin", "editor"]).Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]));

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void A_loading_provider_renders_the_Authorizing_slot()
    {
        var html = Render(new LoadingUser(),
            () => Authorize
                .Authorized(_ => Span["AUTHED"])
                .NotAuthorized(Span["DENIED"])
                .Authorizing(Span["LOADING"]));

        Assert.Contains("LOADING", html);
        Assert.DoesNotContain("AUTHED", html);
        Assert.DoesNotContain("DENIED", html);
    }

    [Fact]
    public void A_policy_that_allows_renders_the_authorized_slot()
    {
        var html = Render(User("root", "admin"),
            () => Authorize.Policy("admins-only").Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]),
            WithAdminsPolicy);

        Assert.Contains("AUTHED", html);
    }

    [Fact]
    public void A_policy_that_denies_renders_the_not_authorized_slot()
    {
        var html = Render(User("alice", "user"),
            () => Authorize.Policy("admins-only").Authorized(_ => Span["AUTHED"]).NotAuthorized(Span["DENIED"]),
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

    private static string Render(IUserProvider provider, Func<Component> gate,
        Action<IServiceCollection>? configure = null)
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
                if (req is RolesAuthorizationRequirement roles
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
