using Microsoft.AspNetCore.Authorization;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Pages;

[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "members", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            Authorize(
                Authorizing: P(Id: "members-authorizing")["Loading…"],
                NotAuthorized: P(Id: "members-anon")["Please ", A(Href: "/login")["sign in"], "."],
                Authorized: MemberContent())
        ];
}

public sealed class MemberContent(JwtLoginService login) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1(Id: "members-greeting")[$"Welcome, {User.Identity?.Name}"],
            Authorize(
                Roles: ["admin"],
                Authorized: Div(Id: "admin-note", Style: "color:#7a5c00")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: login.LogoutAsync)["Sign out"]
        ];
}
