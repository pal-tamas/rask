using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt.Pages;

// Component-gated (not a [Authorize] route attribute): the principal is held in the live session, set from
// the JWT held in ProtectedSessionStorage. The Authorize component re-renders when that principal changes.
[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "members", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            Authorize(
                NotAuthorized: P(Id: "members-anon")["Please ", A(Href: "/login")["sign in"], "."],
                Authorized: MemberContent())
        ];
}

public sealed class MemberContent(ProtectedSessionStorage store, SessionUserProvider users, Navigator nav)
    : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1(Id: "members-greeting")[$"Welcome, {User.Identity?.Name}"],
            P()["Signed in with a JWT held in ", Code()["ProtectedSessionStorage"],
                " — encrypted at rest, decrypted only server-side."],
            Authorize(
                Roles: ["admin"],
                Authorized: Div(Id: "admin-note", Style: "color:#7a5c00")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: SignOutAsync)["Sign out"]
        ];

    private async Task SignOutAsync()
    {
        await store.DeleteAsync("rask.jwt");
        users.Clear();
        nav.Navigate("/login");
    }
}
