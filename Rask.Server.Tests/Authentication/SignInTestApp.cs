using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Server.Tests.Authentication;

public sealed class SignInTestApp(AuthSignIn auth, RouteState routeState) : Component
{
    protected override Component Render() =>
        Fragment(
            Doctype(),
            new Html { Children = [new Head { Children = [new Title { Children = ["auth-test"] }] },
                new Body { Children = [new H1 { Children = [$"path={routeState.Path}"] },
                    new P { Children = [$"user={(User.Identity?.IsAuthenticated == true ? User.Identity.Name : "anon")}"] },
                    Button(OnClickAsync: SignInAsync, Children: ["sign-in"]),
                    Button(OnClickAsync: SignOutAsync, Children: ["sign-out"])] }] });

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
