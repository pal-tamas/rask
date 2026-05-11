using System.Security.Claims;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Server.Tests.Authentication;

public sealed class SignInTestApp(AuthSignIn auth, RouteState routeState) : Component
{
    public override Component Render() =>
        Fragment(
            Doctype(),
            new Html(null,
                new Head(null, new Title(null, "auth-test")),
                new Body(null,
                    new H1(null, $"path={routeState.Path}"),
                    new P(null, $"user={(User.Identity?.IsAuthenticated == true ? User.Identity.Name : "anon")}"),
                    Button(OnClickAsync: SignInAsync, Children: ["sign-in"]),
                    Button(OnClickAsync: SignOutAsync, Children: ["sign-out"]))));

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
