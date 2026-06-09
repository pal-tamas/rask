using Microsoft.AspNetCore.Authorization;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Company.RaskWasm;

[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            Authorize(
                NotAuthorized: P()["Please ", NavLink(Href: "/login")["sign in"], "."],
                Authorized: MemberContent())
        ];
}

public sealed class MemberContent(JwtLoginService login) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1()[$"Welcome, {User.Identity?.Name}"],
            Authorize(
                Roles: ["admin"],
                Authorized: Div(Style: "color:#7a5c00")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: login.LogoutAsync)["Sign out"]
        ];
}
