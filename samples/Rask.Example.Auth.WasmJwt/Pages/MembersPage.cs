using Microsoft.AspNetCore.Authorization;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Pages;

[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "members", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                Authorize(
                    Authorizing: P(Id: "members-authorizing", Class: "text-secondary mb-0")["Loading…"],
                    NotAuthorized: P(Id: "members-anon", Class: "mb-0")[
                        "Please ", NavLink(Href: "/login")["sign in"], "."],
                    Authorized: MemberContent())
            ]
        ];
}

public sealed class MemberContent(JwtLoginService login) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1(Id: "members-greeting", Class: "h3 mb-3")[$"Welcome, {User.Identity?.Name}"],
            Authorize(
                Roles: ["admin"],
                Authorized: Div(Id: "admin-note", Class: "alert alert-warning py-2")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: login.LogoutAsync, Class: "btn btn-outline-primary")["Sign out"]
        ];
}
