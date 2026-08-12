using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Authentication;

// A routed app modelling the real sign-in-then-land flow: a source page whose sign-in handler re-issues
// the cookie with returnUrl pointing at a DIFFERENT page, and a destination page that records the
// principal it observes in OnMount (standing in for a scoped data load). Exercises that the deferred
// auth navigation mounts the destination under the NEW identity, not the pre-SignIn snapshot.
public sealed partial class DeferredAuthNavTestApp : Component
{
    protected override Component? HeadAssets => Title["deferred-auth-nav"];

    protected override Component? Render() => Router;
}

[Route("/start")]
[AllowAnonymous]
public sealed partial class DeferredNavStartPage(AuthSignIn auth) : Component
{
    protected override Component? Render() =>
        Div.Id("start")[Button.OnClickAsync(SignInAsync)["sign-in"]];

    private Task SignInAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(ClaimTypes.NameIdentifier, "alice")
            ],
            "TestCookie");
        return auth.SignInAsync(new ClaimsPrincipal(identity), "/dashboard", "TestCookie");
    }
}

[Route("/dashboard")]
[AllowAnonymous]
public sealed partial class DeferredNavDashboardPage(IUserProvider userProvider) : Component
{
    // Captured once, at mount — the moment a real page would kick off its identity/tenant-scoped load.
    // If the page mounts under the pre-SignIn principal this reads "anon"; under the redeemed identity
    // it reads "alice".
    private string _mountUser = "unset";

    protected override void OnMount() =>
        _mountUser = userProvider.Current.Identity?.IsAuthenticated == true
            ? userProvider.Current.Identity.Name ?? "?"
            : "anon";

    protected override Component? Render() => Div.Id("dash")["mountUser=", _mountUser];
}
