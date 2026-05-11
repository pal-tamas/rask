using Rask.Core;
using Rask.Core.Authentication;
using static Rask.Core.Tags;

namespace Rask.Example.Components;

public sealed class UserBadge(IAuthSignIn auth) : Component
{
    public override Component Render() =>
        User.Identity?.IsAuthenticated == true
            ? Div(Class: "d-flex align-items-center gap-2", Children: [
                Span(Class: "navbar-text", Children: [
                    "Signed in as ", Strong(Children: [User.Identity.Name ?? "user"])
                ]),
                Button(OnClickAsync: HandleSignOutAsync,
                    Class: "btn btn-outline-secondary btn-sm",
                    Children: ["Sign out"])
              ])
            : NavLink("/login", Class: "btn btn-outline-primary btn-sm", Children: ["Sign in"]);

    private Task HandleSignOutAsync() => auth.SignOutAsync("/");
}
