using Microsoft.AspNetCore.Authorization;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Company.RaskWasmHosted.Wasm;

// On WASM there's no server route guard — the Authorize component gates the content off the principal
// (hydrated from /api/me). The signed-in view is a child component so it reads the fresh principal when the
// gate opens after sign-in.
[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            Authorize(
                NotAuthorized: P()["Please ", A(Href: "/login")["sign in"], "."],
                Authorized: MemberContent())
        ];
}

public sealed class MemberContent(WasmLoginService login) : Component
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
