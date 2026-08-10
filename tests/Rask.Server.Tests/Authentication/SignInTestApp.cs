using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Authentication;

public sealed partial class SignInTestApp(AuthSignIn auth, RouteState routeState, IUserProvider userProvider) : Component
{
    protected override Component? Head => new Title()["auth-test"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        new H1()[$"path={routeState.Path}"],
        new P()[$"user={(userProvider.Current.Identity?.IsAuthenticated == true ? userProvider.Current.Identity.Name : "anon")}"],
        Button.OnClickAsync(SignInAsync)["sign-in"],
        Button.OnClickAsync(SignOutAsync)["sign-out"]
    ];

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

    private Task SignOutAsync() => auth.SignOutAsync("/", "TestCookie");
}
