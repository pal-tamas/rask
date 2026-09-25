using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Authentication;

public sealed partial class SignInTestApp(AuthSignIn auth, RouteState routeState, IUserProvider userProvider) : Component
{
    protected override Component? HeadAssets => Markup.Title["auth-test"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.H1[$"path={routeState.Path}"],
        Markup.P[$"user={(userProvider.Current.Identity?.IsAuthenticated == true ? userProvider.Current.Identity.Name : "anon")}"],
        Button.OnClick(SignInAsync)["sign-in"],
        Button.OnClick(SignOutAsync)["sign-out"]
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
