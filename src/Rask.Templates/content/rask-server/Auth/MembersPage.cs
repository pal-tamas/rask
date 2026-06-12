using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Company.RaskServer;

// [Authorize] blocks anonymous deep-links (full GET → 302 to /login). The Authorize component gates the
// content and re-renders when the post-sign-in reconnect re-seeds the principal; the signed-in view lives
// in its own component that injects IUserProvider, so it reads the freshly-authenticated principal — no
// manual Changed subscription.
[Route("members")]
[Authorize]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Class: "welcome-card")[
            Authorize(
                NotAuthorized: P()["Please ", NavLink(Href: "/login")["sign in"], "."])[MemberContent()]
        ];
}

public sealed class MemberContent(IAuthSignIn auth, IUserProvider userProvider) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1()[$"Welcome, {userProvider.Current.Identity?.Name}"],
            Authorize(Roles: ["admin"])[
                Div(Style: "color:#7a5c00")["🔑 You have admin access."]],
            Button(OnClickAsync: () => auth.SignOutAsync(returnUrl: "/login"))["Sign out"]
        ];
}
