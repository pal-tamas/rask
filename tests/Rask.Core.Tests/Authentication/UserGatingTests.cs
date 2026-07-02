using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factory

namespace Rask.Core.Tests.Authentication;

// Injecting IUserProvider and reading .Current (resolved from the active render scope) is the
// imperative way to gate content (the declarative counterpart is the Authorize component — see
// AuthorizeTests). These pin that gating in Render() reflects the provider's principal, incl. roles.
public class UserGatingTests
{
    [Fact]
    public void User_Anonymous_RendersFallback()
    {
        var html = Render(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Contains("public", html);
        Assert.DoesNotContain("secret", html);
    }

    [Fact]
    public void User_Authenticated_RendersGatedContent()
    {
        var html = Render(Principal("alice"));

        Assert.Contains("secret", html);
        Assert.DoesNotContain("public", html);
    }

    [Fact]
    public void User_InRole_RendersRoleGatedContent()
    {
        var html = Render(Principal("admin", "admin"));

        Assert.Contains("admin-panel", html);
    }

    [Fact]
    public void User_NotInRole_HidesRoleGatedContent()
    {
        var html = Render(Principal("alice", "user"));

        Assert.Contains("secret", html); // authenticated content shows
        Assert.DoesNotContain("admin-panel", html); // but not the admin panel
    }

    private static string Render(ClaimsPrincipal principal)
    {
        var provider = new FixedUser(principal);
        var sp = new ServiceCollection()
            .AddSingleton<IUserProvider>(provider)
            .BuildServiceProvider();
        return new StubComponent(() => new Gate(provider)).RenderAsLiveRoot(sp);
    }

    private static ClaimsPrincipal Principal(string name, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        foreach (var r in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, r));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class Gate(IUserProvider provider) : Component
    {
        private ClaimsPrincipal User => provider.Current;

        protected override Component? Render() =>
            User.Identity?.IsAuthenticated == true
                ?
                [
                    Span()["secret"],
                    User.IsInRole("admin") ? Div(Class: "admin-panel")["admin"] : null
                ]
                : Span()["public"];
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
}
