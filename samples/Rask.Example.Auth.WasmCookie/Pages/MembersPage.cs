using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie.Pages;

// On WASM there's no server route guard — the Authorize component gates the content off the ApiUserProvider
// principal (hydrated from /api/me). The signed-in view is a child component so it reads the fresh principal
// when the gate opens after sign-in.
[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "members", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                Authorize(
                    Authorizing: P("members-authorizing", "text-secondary mb-0")["Loading…"],
                    NotAuthorized: P("members-anon", "mb-0")[
                        "Please ", NavLink("/login")["sign in"], "."],
                    Authorized: MemberContent())
            ]
        ];
}

public sealed class MemberContent(WasmLoginService login) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1("members-greeting", "h3 mb-3")[$"Welcome, {User.Identity?.Name}"],
            Authorize(
                ["admin"],
                Authorized: Div(Id: "admin-note", Class: "alert alert-warning py-2")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: login.LogoutAsync, Class: "btn btn-outline-primary")["Sign out"]
        ];
}
