using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Server.Tests.Authentication;

public sealed class SignInTestApp(AuthSignIn auth, RouteState routeState) : Component
{
    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["auth-test"]],
                new Body()[new H1()[$"path={routeState.Path}"],
                    new P()[$"user={(User.Identity?.IsAuthenticated == true ? User.Identity.Name : "anon")}"],
                    Button(OnClickAsync: SignInAsync)["sign-in"],
                    Button(OnClickAsync: SignOutAsync)["sign-out"]]]];

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
